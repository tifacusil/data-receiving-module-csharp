using System.Buffers.Binary;

namespace DataReceiving.Parsing.Payloads;

public static class GnssPayloadDecoder
{
    private static readonly string[] FixQualityMap =
        ["no_fix", "single", "dgps", "rtk_float", "rtk_fixed"];

    public static (ParseResult? Error, GnssPayload? Payload) Decode(ReadOnlySpan<byte> data)
    {
        // 4+4+4+4+4+1 = 21 bytes
        if (data.Length < 21)
            return (Fail("GNSS payload too short, need 21 bytes"), null);

        float dispX = ReadFloat(data[0..4]);
        float dispY = ReadFloat(data[4..8]);
        float dispZ = ReadFloat(data[8..12]);
        float dispH = ReadFloat(data[12..16]);
        float cumul = ReadFloat(data[16..20]);
        byte fixByte = data[20];

        if (dispH < 0)
            return (Fail($"displacement_horizontal_mm {dispH} must be >= 0"), null);
        if (cumul < 0)
            return (Fail($"cumulative_displacement_mm {cumul} must be >= 0"), null);
        if (fixByte > 4)
            return (Fail($"Unknown fix_quality byte 0x{fixByte:X2}"), null);

        return (null, new GnssPayload(dispX, dispY, dispZ, dispH, cumul, FixQualityMap[fixByte]));
    }

    private static float ReadFloat(ReadOnlySpan<byte> b) =>
        BinaryPrimitives.ReadSingleBigEndian(b);

    private static ParseResult Fail(string msg) =>
        new(false, null, new ParseError(ParseStage.PayloadDecode, msg));
}
