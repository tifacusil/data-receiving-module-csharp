using DataReceiving.Buffer;
using DataReceiving.Messaging;
using DataReceiving.Parsing;
using Microsoft.Extensions.Logging;

namespace DataReceiving.Sinks;

public sealed class RabbitMqSink : ISink
{
    private readonly IMessageBuffer _buffer;
    private readonly IMessagePublisher _publisher;
    private readonly ILogger<RabbitMqSink> _logger;

    public RabbitMqSink(IMessageBuffer buffer, IMessagePublisher publisher, ILogger<RabbitMqSink> logger)
    {
        _buffer = buffer;
        _publisher = publisher;
        _logger = logger;
    }

    public async Task WriteAsync(SensorEnvelope envelope, CancellationToken ct)
    {
        var routingKey = $"sensors.{envelope.ProjectId}.{envelope.SensorType}";
        var json = EnvelopeSerializer.Serialize(envelope);

        await _buffer.SetAsync(envelope.MessageId, routingKey, json, ct);

        try
        {
            await _publisher.PublishJsonAsync(routingKey, json, envelope.MessageId, ct);
            await _buffer.DeleteAsync(envelope.MessageId, ct);
            _logger.LogDebug("published {SensorType}/{SensorId} routing={RoutingKey} msg={MessageId}",
                envelope.SensorType, envelope.SensorId, routingKey, envelope.MessageId);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "rabbitmq publish failed for msg={MessageId}; buffered in redis for retry",
                envelope.MessageId);
        }
    }
}
