using System.Text;
using System.Text.RegularExpressions;

namespace DataReceiving.Parsing;

public static class EnvelopeDecoder
{
    private static readonly Regex ProjectIdPattern = new(@"^[a-z][a-z0-9_]{2,49}$", RegexOptions.Compiled);

    /// <summary>
    /// Decodes the common header from the payload region (bytes 6..N-4 of the full frame).
    /// Returns (headerByteCount, partial envelope) or an error result.
    /// </summary>
    public static (ParseResult? Error, int HeaderLength, string MessageId, string ProjectId, string SensorId, string Timestamp, int SamplingInterval) Decode(
        ReadOnlySpan<byte> payload)
    {
        static (ParseResult?, int, string, string, string, string, int) Fail(ParseStage stage, string msg) =>
            (new ParseResult(false, null, new ParseError(stage, msg)), 0, "", "", "", "", 0);

        int offset = 0;

        // MSG_ID: 16 bytes UUID
        if (payload.Length < 16)
            return Fail(ParseStage.HeaderDecode, "Payload too short for MSG_ID");
        var msgIdBytes = payload[0..16];
        string messageId = FormatUuid(msgIdBytes);
        offset += 16;

        // PROJ_LEN: uint8
        if (payload.Length < offset + 1)
            return Fail(ParseStage.HeaderDecode, "Payload too short for PROJ_LEN");
        int projLen = payload[offset++];
        if (projLen < 3 || projLen > 50)
            return Fail(ParseStage.HeaderDecode, $"project_id length {projLen} out of range [3..50]");

        // PROJ_ID
        if (payload.Length < offset + projLen)
            return Fail(ParseStage.HeaderDecode, "Payload too short for PROJ_ID");
        string projectId = Encoding.ASCII.GetString(payload[offset..(offset + projLen)]);
        offset += projLen;
        if (!ProjectIdPattern.IsMatch(projectId))
            return Fail(ParseStage.HeaderDecode, $"project_id '{projectId}' does not match pattern ^[a-z][a-z0-9_]{{2,49}}$");

        // SENS_LEN: uint8
        if (payload.Length < offset + 1)
            return Fail(ParseStage.HeaderDecode, "Payload too short for SENS_LEN");
        int sensLen = payload[offset++];
        if (sensLen < 1 || sensLen > 64)
            return Fail(ParseStage.HeaderDecode, $"sensor_id length {sensLen} out of range [1..64]");

        // SENS_ID
        if (payload.Length < offset + sensLen)
            return Fail(ParseStage.HeaderDecode, "Payload too short for SENS_ID");
        string sensorId = Encoding.ASCII.GetString(payload[offset..(offset + sensLen)]);
        offset += sensLen;

        // TIMESTAMP: int64 BE (Unix epoch ms UTC)
        if (payload.Length < offset + 8)
            return Fail(ParseStage.HeaderDecode, "Payload too short for TIMESTAMP");
        long epochMs = ReadInt64Be(payload[offset..(offset + 8)]);
        offset += 8;
        string timestamp = DateTimeOffset.FromUnixTimeMilliseconds(epochMs)
            .UtcDateTime.ToString("yyyy-MM-ddTHH:mm:ssZ");

        // SAMPLE_INT: uint32 BE
        if (payload.Length < offset + 4)
            return Fail(ParseStage.HeaderDecode, "Payload too short for SAMPLE_INT");
        long samplingInterval = ReadUInt32Be(payload[offset..(offset + 4)]);
        offset += 4;
        if (samplingInterval < 1 || samplingInterval > 86400)
            return Fail(ParseStage.HeaderDecode, $"sampling_interval_seconds {samplingInterval} out of range [1..86400]");

        return (null, offset, messageId, projectId, sensorId, timestamp, (int)samplingInterval);
    }

    private static string FormatUuid(ReadOnlySpan<byte> b)
    {
        return $"{b[0]:x2}{b[1]:x2}{b[2]:x2}{b[3]:x2}-" +
               $"{b[4]:x2}{b[5]:x2}-" +
               $"{b[6]:x2}{b[7]:x2}-" +
               $"{b[8]:x2}{b[9]:x2}-" +
               $"{b[10]:x2}{b[11]:x2}{b[12]:x2}{b[13]:x2}{b[14]:x2}{b[15]:x2}";
    }

    private static long ReadInt64Be(ReadOnlySpan<byte> b) =>
        ((long)b[0] << 56) | ((long)b[1] << 48) | ((long)b[2] << 40) | ((long)b[3] << 32) |
        ((long)b[4] << 24) | ((long)b[5] << 16) | ((long)b[6] << 8) | b[7];

    private static uint ReadUInt32Be(ReadOnlySpan<byte> b) =>
        ((uint)b[0] << 24) | ((uint)b[1] << 16) | ((uint)b[2] << 8) | b[3];
}
