using DataReceiving.Parsing;

namespace DataReceiving.Sinks;

public class FileSink : ISink
{
    private readonly string _outputPath;

    public FileSink(string outputPath)
    {
        _outputPath = outputPath;
        Directory.CreateDirectory(outputPath);
    }

    public async Task WriteAsync(SensorEnvelope envelope, CancellationToken ct)
    {
        string json = EnvelopeSerializer.Serialize(envelope);
        string path = Path.Combine(_outputPath, $"{envelope.MessageId}.json");
        await File.WriteAllTextAsync(path, json, ct);
    }
}
