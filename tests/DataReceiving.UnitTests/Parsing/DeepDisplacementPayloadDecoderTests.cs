using System.Buffers.Binary;
using DataReceiving.Parsing;
using DataReceiving.Parsing.Payloads;

namespace DataReceiving.UnitTests.Parsing;

public class DeepDisplacementPayloadDecoderTests
{
    private static byte[] BuildDeepDispBytes(float depth, float tiltX, float tiltY, float cumul)
    {
        var buf = new byte[16];
        BinaryPrimitives.WriteSingleBigEndian(buf.AsSpan(0, 4), depth);
        BinaryPrimitives.WriteSingleBigEndian(buf.AsSpan(4, 4), tiltX);
        BinaryPrimitives.WriteSingleBigEndian(buf.AsSpan(8, 4), tiltY);
        BinaryPrimitives.WriteSingleBigEndian(buf.AsSpan(12, 4), cumul);
        return buf;
    }

    [Fact]
    public void test_valid_deep_displacement()
    {
        var data = BuildDeepDispBytes(15.0f, 3.21f, -1.54f, 48.7f);
        var (err, payload) = DeepDisplacementPayloadDecoder.Decode(data);
        Assert.Null(err);
        Assert.NotNull(payload);
        Assert.Equal(15.0f, payload!.DepthM, precision: 1);
        Assert.Equal(3.21f, payload.TiltXMm, precision: 2);
        Assert.Equal(-1.54f, payload.TiltYMm, precision: 2);
        Assert.Equal(48.7f, payload.CumulativeTiltMm, precision: 1);
    }

    [Fact]
    public void test_negative_depth_rejected()
    {
        var data = BuildDeepDispBytes(-1.0f, 0, 0, 0);
        var (err, _) = DeepDisplacementPayloadDecoder.Decode(data);
        Assert.NotNull(err);
        Assert.Equal(ParseStage.PayloadDecode, err!.Error!.Stage);
    }

    [Fact]
    public void test_tilt_y_negative_accepted()
    {
        var data = BuildDeepDispBytes(8.0f, 12.3f, -3.4f, 89.1f);
        var (err, payload) = DeepDisplacementPayloadDecoder.Decode(data);
        Assert.Null(err);
        Assert.Equal(-3.4f, payload!.TiltYMm, precision: 1);
    }
}
