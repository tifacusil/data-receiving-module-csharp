using System.Text;
using System.Threading.Channels;
using DataReceiving.Configuration;
using DataReceiving.Parsing;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using RabbitMQ.Client;

namespace DataReceiving.Messaging;

public sealed class RabbitMqPublisher : IMessagePublisher, IDisposable
{
    private readonly IModel _channel;
    private readonly RabbitMqOptions _opts;
    private readonly ILogger<RabbitMqPublisher> _logger;
    private readonly Channel<PublishRequest> _queue;
    private readonly CancellationTokenSource _cts = new();
    private readonly Task _worker;

    public RabbitMqPublisher(IModel channel, IOptions<ReceiverOptions> options, ILogger<RabbitMqPublisher> logger)
    {
        _channel = channel;
        _opts = options.Value.RabbitMq;
        _logger = logger;

        // IModel is not thread-safe; a single dedicated consumer serializes all
        // publishes so no two threads ever touch _channel concurrently.
        _queue = Channel.CreateUnbounded<PublishRequest>(new UnboundedChannelOptions { SingleReader = true });
        _worker = Task.Run(() => ProcessQueueAsync(_cts.Token));
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
                try
                {
                    var body = Encoding.UTF8.GetBytes(request.EnvelopeJson);
                    var props = _channel.CreateBasicProperties();
                    props.DeliveryMode = 2;
                    props.ContentType = "application/json";
                    props.MessageId = request.MessageId;

                    _channel.BasicPublish(
                        exchange: _opts.Exchange,
                        routingKey: request.RoutingKey,
                        basicProperties: props,
                        body: body);

                    _channel.WaitForConfirmsOrDie(TimeSpan.FromSeconds(_opts.ConfirmTimeoutSeconds));

                    _logger.LogDebug("published msg={MessageId} routingKey={RoutingKey}", request.MessageId, request.RoutingKey);
                    request.Completion.TrySetResult();
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "publish failed msg={MessageId} routingKey={RoutingKey}", request.MessageId, request.RoutingKey);
                    request.Completion.TrySetException(ex);
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
        _cts.Cancel();
        try { _worker.Wait(TimeSpan.FromSeconds(5)); }
        catch { /* best-effort drain on shutdown */ }
        _cts.Dispose();
    }

    private sealed record PublishRequest(string RoutingKey, string EnvelopeJson, string MessageId, TaskCompletionSource Completion);
}
