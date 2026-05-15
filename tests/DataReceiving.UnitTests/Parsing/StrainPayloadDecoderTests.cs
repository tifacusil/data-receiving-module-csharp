using System.Buffers.Binary;
using DataReceiving.Parsing;
using DataReceiving.Parsing.Payloads;

namespace DataReceiving.UnitTests.Parsing;

public class StrainPayloadDecoderTests
{
    private static byte[] BuildStrainBytes(float strain, float? temp)
    {
        bool hasTemp = temp.HasValue;
        var buf = new byte[hasTemp ? 9 : 5];
        buf[0] = hasTemp ? (byte)0x01 : (byte)0x00;
        BinaryPrimitives.WriteSingleBigEndian(buf.AsSpan(1, 4), strain);
        if (hasTemp)
            BinaryPrimitives.WriteSingleBigEndian(buf.AsSpan(5, 4), temp!.Value);
        return buf;
    }

    [Fact]
    public void test_strain_with_temperature()
    {
        var data = BuildStrainBytes(234.5f, 24.3f);
        var (err, payload) = StrainPayloadDecoder.Decode(data);
        Assert.Null(err);
        Assert.NotNull(payload);
        Assert.Equal(234.5f, payload!.StrainMicrostrain, precision: 1);
        Assert.NotNull(payload.TemperatureCelsius);
        Assert.Equal(24.3f, payload.TemperatureCelsius!.Value, precision: 1);
    }

    [Fact]
    public void test_strain_without_temperature()
    {
        var data = BuildStrainBytes(234.5f, null);
        var (err, payload) = StrainPayloadDecoder.Decode(data);
        Assert.Null(err);
        Assert.NotNull(payload);
        Assert.Null(payload!.TemperatureCelsius);
    }

    [Fact]
    public void test_temperature_below_minimum_rejected()
    {
        var data = BuildStrainBytes(100f, -41.0f);
        var (err, _) = StrainPayloadDecoder.Decode(data);
        Assert.NotNull(err);
        Assert.Equal(ParseStage.PayloadDecode, err!.Error!.Stage);
    }

    [Fact]
    public void test_temperature_above_maximum_rejected()
    {
        var data = BuildStrainBytes(100f, 81.0f);
        var (err, _) = StrainPayloadDecoder.Decode(data);
        Assert.NotNull(err);
        Assert.Equal(ParseStage.PayloadDecode, err!.Error!.Stage);
    }
}
