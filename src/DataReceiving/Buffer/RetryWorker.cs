using DataReceiving.Configuration;
using DataReceiving.Messaging;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace DataReceiving.Buffer;

public sealed class RetryWorker : BackgroundService
{
    private readonly IMessageBuffer _buffer;
    private readonly IMessagePublisher _publisher;
    private readonly RedisOptions _opts;
    private readonly ILogger<RetryWorker> _logger;

    public RetryWorker(IMessageBuffer buffer, IMessagePublisher publisher, IOptions<ReceiverOptions> options, ILogger<RetryWorker> logger)
    {
        _buffer = buffer;
        _publisher = publisher;
        _opts = options.Value.Redis;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var interval = TimeSpan.FromSeconds(_opts.RetryIntervalSeconds);
        var minAge = TimeSpan.FromMinutes(_opts.RetryMinAgeMinutes);

        while (!stoppingToken.IsCancellationRequested)
        {
            try { await Task.Delay(interval, stoppingToken); }
            catch (OperationCanceledException) { break; }

            await RunRetryPassAsync(minAge, stoppingToken);
        }
    }

    internal async Task RunRetryPassAsync(TimeSpan minAge, CancellationToken ct)
    {
        var count = await _buffer.CountAsync(ct);
        if (count > 0)
            _logger.LogInformation("retry pass: {Count} messages in redis buffer", count);

        var retried = 0;
        var succeeded = 0;

        await foreach (var msg in _buffer.ScanOldAsync(minAge, ct))
        {
            retried++;
            try
            {
                await _publisher.PublishJsonAsync(msg.RoutingKey, msg.EnvelopeJson, msg.MessageId, ct);
                await _buffer.DeleteAsync(msg.MessageId, ct);
                succeeded++;
                _logger.LogInformation("retry succeeded: msg={MessageId} age={Age:mm\\:ss}",
                    msg.MessageId, DateTime.UtcNow - msg.StoredAt);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "retry failed: msg={MessageId}; will retry in {Interval}s",
                    msg.MessageId, _opts.RetryIntervalSeconds);
            }
        }

        if (retried > 0)
            _logger.LogInformation("retry pass complete: {Retried} retried, {Succeeded} published", retried, succeeded);
    }
}
