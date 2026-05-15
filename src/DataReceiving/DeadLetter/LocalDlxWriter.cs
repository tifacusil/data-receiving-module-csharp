using System.Text.Json;
using DataReceiving.Parsing;

namespace DataReceiving.DeadLetter;

public class LocalDlxWriter
{
    private readonly string _dlxPath;

    public LocalDlxWriter(string dlxPath)
    {
        _dlxPath = dlxPath;
        Directory.CreateDirectory(dlxPath);
    }

    public async Task WriteAsync(ReadOnlyMemory<byte> rawFrame, ParseError error, CancellationToken ct = default)
    {
        var entry = new DlxEntry(
            ReceivedAt: DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ss.fffZ"),
            RawFrameHex: Convert.ToHexString(rawFrame.Span),
            Stage: error.Stage.ToString(),
            ErrorMessage: error.Message,
            AckStatus: AckStatusFor(error.Stage)
        );
        string json = JsonSerializer.Serialize(entry, new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
            WriteIndented = true,
        });
        string fileName = $"{DateTime.UtcNow:yyyyMMddHHmmssfff}_{Guid.NewGuid():N}.json";
        await File.WriteAllTextAsync(Path.Combine(_dlxPath, fileName), json, ct);
    }

    private static int AckStatusFor(ParseStage stage) => stage switch
    {
        ParseStage.Crc => 1,
        ParseStage.SensorType => 4,
        ParseStage.SchemaValidation => 3,
        _ => 2,
    };

    private record DlxEntry(
        string ReceivedAt,
        string RawFrameHex,
        string Stage,
        string ErrorMessage,
        int AckStatus
    );
}
