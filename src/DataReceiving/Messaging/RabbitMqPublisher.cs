using System.Collections.Concurrent;
using System.Text;
using System.Threading.Channels;
using DataReceiving.Configuration;
using DataReceiving.Parsing;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using RabbitMQ.Client;
using RabbitMQ.Client.Events;

namespace DataReceiving.Messaging;

public sealed class RabbitMqPublisher : IMessagePublisher, IDisposable
{
    // Cap on unconfirmed publishes so a slow/unresponsive broker applies
    // backpressure instead of growing the pending map without bound.
    private const int MaxInFlight = 512;

    private readonly IModel _channel;
    private readonly RabbitMqOptions _opts;
    private readonly ILogger<RabbitMqPublisher> _logger;
    private readonly Channel<PublishRequest> _queue;
    private readonly CancellationTokenSource _cts = new();
    private readonly Task _worker;
    private readonly Task _sweeper;
    private readonly SemaphoreSlim _inFlight = new(MaxInFlight);
    private readonly ConcurrentDictionary<ulong, PendingConfirm> _pending = new();

    public RabbitMqPublisher(IModel channel, IOptions<ReceiverOptions> options, ILogger<RabbitMqPublisher> logger)
    {
        _channel = channel;
        _opts = options.Value.RabbitMq;
        _logger = logger;

        _channel.BasicAcks += OnBasicAcks;
        _channel.BasicNacks += OnBasicNacks;
        _channel.ModelShutdown += OnModelShutdown;

        // IModel is not thread-safe; a single dedicated consumer serializes all
        // publishes so no two threads ever touch _channel concurrently. Confirms
        // are tracked per sequence number and resolved from the BasicAcks/BasicNacks
        // callbacks, so the worker never blocks on a broker round-trip per message.
        _queue = Channel.CreateUnbounded<PublishRequest>(new UnboundedChannelOptions { SingleReader = true });
        _worker = Task.Run(() => ProcessQueueAsync(_cts.Token));
        _sweeper = Task.Run(() => SweepExpiredAsync(_cts.Token));
    }

    public Task PublishAsync(SensorEnvelope envelope, CancellationToken ct)
    {
        var routingKey = $"sensors.{envelope.ProjectId}.{envelope.SensorType}";
        var json = EnvelopeSerializer.Serialize(envelope);
        return PublishJsonAsync(routingKey, json, envelope.MessageId, ct);
    }

    public Task PublishJsonAsync(string routingKey, string envelopeJson, string messageId, CancellationToken ct)
    {
        var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var request = new PublishRequest(routingKey, envelopeJson, messageId, completion);

        if (!_queue.Writer.TryWrite(request))
        {
            completion.TrySetException(new ObjectDisposedException(nameof(RabbitMqPublisher), "Publish queue is closed"));
        }

        return completion.Task.WaitAsync(ct);
    }

    private async Task ProcessQueueAsync(CancellationToken stoppingToken)
    {
        try
        {
            await foreach (var request in _queue.Reader.ReadAllAsync(stoppingToken))
            {
                await _inFlight.WaitAsync(stoppingToken);

                ulong seqNo = 0;
                try
                {
                    var body = Encoding.UTF8.GetBytes(request.EnvelopeJson);
                    var props = _channel.CreateBasicProperties();
                    props.DeliveryMode = 2;
                    props.ContentType = "application/json";
                    props.MessageId = request.MessageId;

                    // NextPublishSeqNo is 0 when the channel is not in confirm mode;
                    // in that case the publish completes as soon as it is written.
                    seqNo = _channel.NextPublishSeqNo;
                    if (seqNo > 0)
                    {
                        var deadline = DateTime.UtcNow.AddSeconds(_opts.ConfirmTimeoutSeconds);
                        _pending[seqNo] = new PendingConfirm(request, deadline);
                    }

                    _channel.BasicPublish(
                        exchange: _opts.Exchange,
                        routingKey: request.RoutingKey,
                        basicProperties: props,
                        body: body);

                    _logger.LogDebug("published msg={MessageId} routingKey={RoutingKey} seq={SeqNo}",
                        request.MessageId, request.RoutingKey, seqNo);

                    if (seqNo == 0)
                    {
                        _inFlight.Release();
                        request.Completion.TrySetResult();
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "publish failed msg={MessageId} routingKey={RoutingKey}",
                        request.MessageId, request.RoutingKey);
                    if (seqNo > 0)
                        _pending.TryRemove(seqNo, out _);
                    _inFlight.Release();
                    request.Completion.TrySetException(ex);
                }
            }
        }
        catch (OperationCanceledException)
        {
            // shutting down
        }
    }

    private void OnBasicAcks(object? sender, BasicAckEventArgs e) =>
        Settle(e.DeliveryTag, e.Multiple, error: null);

    private void OnBasicNacks(object? sender, BasicNackEventArgs e) =>
        Settle(e.DeliveryTag, e.Multiple, new IOException($"Broker nacked publish seq={e.DeliveryTag} multiple={e.Multiple}"));

    private void OnModelShutdown(object? sender, ShutdownEventArgs e)
    {
        var error = new IOException($"Channel shut down before confirm: {e.ReplyText}");
        foreach (var key in _pending.Keys)
        {
            if (_pending.TryRemove(key, out var entry))
                Finish(entry, error);
        }
    }

    private void Settle(ulong deliveryTag, bool multiple, Exception? error)
    {
        if (multiple)
        {
            foreach (var key in _pending.Keys)
            {
                if (key <= deliveryTag && _pending.TryRemove(key, out var entry))
                    Finish(entry, error);
            }
        }
        else if (_pending.TryRemove(deliveryTag, out var entry))
        {
            Finish(entry, error);
        }
    }

    private void Finish(PendingConfirm entry, Exception? error)
    {
        _inFlight.Release();
        if (error is null)
            entry.Request.Completion.TrySetResult();
        else
            entry.Request.Completion.TrySetException(error);
    }

    private async Task SweepExpiredAsync(CancellationToken ct)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(1));
        try
        {
            while (await timer.WaitForNextTickAsync(ct))
            {
                var now = DateTime.UtcNow;
                foreach (var kv in _pending)
                {
                    if (kv.Value.Deadline < now && _pending.TryRemove(kv.Key, out var entry))
                    {
                        _logger.LogWarning("confirm timeout msg={MessageId} seq={SeqNo}",
                            entry.Request.MessageId, kv.Key);
                        Finish(entry, new TimeoutException(
                            $"No publisher confirm within {_opts.ConfirmTimeoutSeconds}s for msg={entry.Request.MessageId}"));
                    }
                }
            }
        }
        catch (OperationCanceledException)
        {
            // shutting down
        }
    }

    public void Dispose()
    {
        _queue.Writer.TryComplete();
        try { _worker.Wait(TimeSpan.FromSeconds(5)); }
        catch { /* best-effort drain on shutdown */ }

        _cts.Cancel();
        try { _sweeper.Wait(TimeSpan.FromSeconds(2)); }
        catch { /* best-effort */ }

        _channel.BasicAcks -= OnBasicAcks;
        _channel.BasicNacks -= OnBasicNacks;
        _channel.ModelShutdown -= OnModelShutdown;

        var shutdownError = new ObjectDisposedException(nameof(RabbitMqPublisher));
        foreach (var key in _pending.Keys)
        {
            if (_pending.TryRemove(key, out var entry))
                entry.Request.Completion.TrySetException(shutdownError);
        }
        while (_queue.Reader.TryRead(out var leftover))
            leftover.Completion.TrySetException(shutdownError);

        _cts.Dispose();
    }

    private sealed record PublishRequest(string RoutingKey, string EnvelopeJson, string MessageId, TaskCompletionSource Completion);

    private sealed record PendingConfirm(PublishRequest Request, DateTime Deadline);
}
