using DataReceiving.Parsing;

namespace DataReceiving.Sinks;

public interface ISink
{
    Task WriteAsync(SensorEnvelope envelope, CancellationToken ct);
}
