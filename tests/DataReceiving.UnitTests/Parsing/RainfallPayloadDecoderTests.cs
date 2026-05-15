using System.Buffers.Binary;
using DataReceiving.Parsing;
using DataReceiving.Parsing.Payloads;

namespace DataReceiving.UnitTests.Parsing;

public class RainfallPayloadDecoderTests
{
    private static byte[] BuildRainfallBytes(float rainfallMm, float cumulMm, ushort intervalMin)
    {
        var buf = new byte[10];
        BinaryPrimitives.WriteSingleBigEndian(buf.AsSpan(0, 4), rainfallMm);
        BinaryPrimitives.WriteSingleBigEndian(buf.AsSpan(4, 4), cumulMm);
        BinaryPrimitives.WriteUInt16BigEndian(buf.AsSpan(8, 2), intervalMin);
        return buf;
    }

    [Fact]
    public void test_valid_rainfall_payload()
    {
        var data = BuildRainfallBytes(2.4f, 1547.2f, 60);
        var (err, payload) = RainfallPayloadDecoder.Decode(data);
        Assert.Null(err);
        Assert.NotNull(payload);
        Assert.Equal(2.4f, payload!.RainfallMm, precision: 2);
        Assert.Equal(1547.2f, payload.CumulativeRainfallMm, precision: 1);
        Assert.Equal(60, payload.IntervalMinutes);
    }

    [Fact]
    public void test_negative_rainfall_rejected()
    {
        var data = BuildRainfallBytes(-1.0f, 0, 60);
        var (err, _) = RainfallPayloadDecoder.Decode(data);
        Assert.NotNull(err);
        Assert.Equal(ParseStage.PayloadDecode, err!.Error!.Stage);
    }

    [Fact]
    public void test_interval_zero_rejected()
    {
        var data = BuildRainfallBytes(0, 0, 0);
        var (err, _) = RainfallPayloadDecoder.Decode(data);
        Assert.NotNull(err);
        Assert.Equal(ParseStage.PayloadDecode, err!.Error!.Stage);
    }

    [Fact]
    public void test_interval_exceeds_max_rejected()
    {
        var data = BuildRainfallBytes(0, 0, 1441);
        var (err, _) = RainfallPayloadDecoder.Decode(data);
        Assert.NotNull(err);
        Assert.Equal(ParseStage.PayloadDecode, err!.Error!.Stage);
    }
}
