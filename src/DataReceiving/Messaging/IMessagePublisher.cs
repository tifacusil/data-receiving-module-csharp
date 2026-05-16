using DataReceiving.Parsing;

namespace DataReceiving.Messaging;

public interface IMessagePublisher
{
    Task PublishAsync(SensorEnvelope envelope, CancellationToken ct);
    Task PublishJsonAsync(string routingKey, string envelopeJson, string messageId, CancellationToken ct);
}
