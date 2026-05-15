using System.Buffers.Binary;

namespace DataReceiving.Parsing.Payloads;

public static class DeepDisplacementPayloadDecoder
{
    public static (ParseResult? Error, DeepDisplacementPayload? Payload) Decode(ReadOnlySpan<byte> data)
    {
        // depth(4) + tiltX(4) + tiltY(4) + cumul(4) = 16 bytes
        if (data.Length < 16)
            return (Fail("Deep displacement payload too short, need 16 bytes"), null);

        float depth = BinaryPrimitives.ReadSingleBigEndian(data[0..4]);
        float tiltX = BinaryPrimitives.ReadSingleBigEndian(data[4..8]);
        float tiltY = BinaryPrimitives.ReadSingleBigEndian(data[8..12]);
        float cumul = BinaryPrimitives.ReadSingleBigEndian(data[12..16]);

        if (depth < 0)
            return (Fail($"depth_m {depth} must be >= 0"), null);

        return (null, new DeepDisplacementPayload(depth, tiltX, tiltY, cumul));
    }

    private static ParseResult Fail(string msg) =>
        new(false, null, new ParseError(ParseStage.PayloadDecode, msg));
}
