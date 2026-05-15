using System.Buffers.Binary;
using DataReceiving.Parsing;
using DataReceiving.Parsing.Payloads;

namespace DataReceiving.UnitTests.Parsing;

public class GnssPayloadDecoderTests
{
    private static byte[] BuildGnssBytes(float dispX, float dispY, float dispZ, float dispH, float cumul, byte fix)
    {
        var buf = new byte[21];
        BinaryPrimitives.WriteSingleBigEndian(buf.AsSpan(0, 4), dispX);
        BinaryPrimitives.WriteSingleBigEndian(buf.AsSpan(4, 4), dispY);
        BinaryPrimitives.WriteSingleBigEndian(buf.AsSpan(8, 4), dispZ);
        BinaryPrimitives.WriteSingleBigEndian(buf.AsSpan(12, 4), dispH);
        BinaryPrimitives.WriteSingleBigEndian(buf.AsSpan(16, 4), cumul);
        buf[20] = fix;
        return buf;
    }

    [Fact]
    public void test_valid_gnss_payload()
    {
        var data = BuildGnssBytes(12.34f, -5.67f, 2.10f, 13.57f, 1247.5f, 0x04);
        var (err, payload) = GnssPayloadDecoder.Decode(data);
        Assert.Null(err);
        Assert.NotNull(payload);
        Assert.Equal(12.34f, payload!.DisplacementXMm, precision: 2);
        Assert.Equal(-5.67f, payload.DisplacementYMm, precision: 2);
        Assert.Equal(2.10f, payload.DisplacementZMm, precision: 2);
        Assert.Equal(13.57f, payload.DisplacementHorizontalMm, precision: 2);
        Assert.Equal(1247.5f, payload.CumulativeDisplacementMm, precision: 1);
        Assert.Equal("rtk_fixed", payload.FixQuality);
    }

    [Theory]
    [InlineData(0x00, "no_fix")]
    [InlineData(0x01, "single")]
    [InlineData(0x02, "dgps")]
    [InlineData(0x03, "rtk_float")]
    [InlineData(0x04, "rtk_fixed")]
    public void test_all_fix_quality_values(byte fixByte, string expected)
    {
        var data = BuildGnssBytes(0, 0, 0, 0, 0, fixByte);
        var (err, payload) = GnssPayloadDecoder.Decode(data);
        Assert.Null(err);
        Assert.Equal(expected, payload!.FixQuality);
    }

    [Fact]
    public void test_negative_horizontal_rejected()
    {
        var data = BuildGnssBytes(0, 0, 0, -1.5f, 0, 0x04);
        var (err, _) = GnssPayloadDecoder.Decode(data);
        Assert.NotNull(err);
        Assert.Equal(ParseStage.PayloadDecode, err!.Error!.Stage);
    }

    [Fact]
    public void test_negative_cumulative_rejected()
    {
        var data = BuildGnssBytes(0, 0, 0, 0, -0.1f, 0x04);
        var (err, _) = GnssPayloadDecoder.Decode(data);
        Assert.NotNull(err);
        Assert.Equal(ParseStage.PayloadDecode, err!.Error!.Stage);
    }

    [Fact]
    public void test_unknown_fix_byte_rejected()
    {
        var data = BuildGnssBytes(0, 0, 0, 0, 0, 0x05);
        var (err, _) = GnssPayloadDecoder.Decode(data);
        Assert.NotNull(err);
        Assert.Equal(ParseStage.PayloadDecode, err!.Error!.Stage);
    }
}
