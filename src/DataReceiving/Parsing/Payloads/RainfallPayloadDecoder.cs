using System.Buffers.Binary;

namespace DataReceiving.Parsing.Payloads;

public static class RainfallPayloadDecoder
{
    public static (ParseResult? Error, RainfallPayload? Payload) Decode(ReadOnlySpan<byte> data)
    {
        // 4+4+2 = 10 bytes
        if (data.Length < 10)
            return (Fail("Rainfall payload too short, need 10 bytes"), null);

        float rainfallMm = BinaryPrimitives.ReadSingleBigEndian(data[0..4]);
        float cumulMm = BinaryPrimitives.ReadSingleBigEndian(data[4..8]);
        int intervalMin = (data[8] << 8) | data[9];

        if (rainfallMm < 0)
            return (Fail($"rainfall_mm {rainfallMm} must be >= 0"), null);
        if (cumulMm < 0)
            return (Fail($"cumulative_rainfall_mm {cumulMm} must be >= 0"), null);
        if (intervalMin < 1 || intervalMin > 1440)
            return (Fail($"interval_minutes {intervalMin} out of range [1..1440]"), null);

        return (null, new RainfallPayload(rainfallMm, cumulMm, intervalMin));
    }

    private static ParseResult Fail(string msg) =>
        new(false, null, new ParseError(ParseStage.PayloadDecode, msg));
}
