using DataReceiving.Parsing;

namespace DataReceiving.Sinks;

public class NullSink : ISink
{
    public Task WriteAsync(SensorEnvelope envelope, CancellationToken ct) => Task.CompletedTask;
}
