namespace DataReceiving.Buffer;

public record BufferedMessage(string MessageId, string RoutingKey, string EnvelopeJson, DateTime StoredAt);

public interface IMessageBuffer
{
    Task SetAsync(string messageId, string routingKey, string envelopeJson, CancellationToken ct);
    Task DeleteAsync(string messageId, CancellationToken ct);
    IAsyncEnumerable<BufferedMessage> ScanOldAsync(TimeSpan minAge, CancellationToken ct);
    Task<long> CountAsync(CancellationToken ct);
}
