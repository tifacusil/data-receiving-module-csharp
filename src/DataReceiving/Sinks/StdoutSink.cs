using DataReceiving.Parsing;

namespace DataReceiving.Sinks;

public class StdoutSink : ISink
{
    public Task WriteAsync(SensorEnvelope envelope, CancellationToken ct)
    {
        Console.WriteLine(EnvelopeSerializer.Serialize(envelope));
        return Task.CompletedTask;
    }
}
