using System.Buffers.Binary;
using DataReceiving.Parsing;
using DataReceiving.Parsing.Payloads;

namespace DataReceiving.UnitTests.Parsing;

public class SoilPressurePayloadDecoderTests
{
    private static byte[] BuildSoilPressureBytes(float pressure, float? temp)
    {
        bool hasTemp = temp.HasValue;
        var buf = new byte[hasTemp ? 9 : 5];
        buf[0] = hasTemp ? (byte)0x01 : (byte)0x00;
        BinaryPrimitives.WriteSingleBigEndian(buf.AsSpan(1, 4), pressure);
        if (hasTemp)
            BinaryPrimitives.WriteSingleBigEndian(buf.AsSpan(5, 4), temp!.Value);
        return buf;
    }

    [Fact]
    public void test_soil_pressure_with_temperature()
    {
        var data = BuildSoilPressureBytes(45.2f, 18.7f);
        var (err, payload) = SoilPressurePayloadDecoder.Decode(data);
        Assert.Null(err);
        Assert.NotNull(payload);
        Assert.Equal(45.2f, payload!.PressureKpa, precision: 1);
        Assert.NotNull(payload.TemperatureCelsius);
        Assert.Equal(18.7f, payload.TemperatureCelsius!.Value, precision: 1);
    }

    [Fact]
    public void test_soil_pressure_without_temperature()
    {
        var data = BuildSoilPressureBytes(45.2f, null);
        var (err, payload) = SoilPressurePayloadDecoder.Decode(data);
        Assert.Null(err);
        Assert.NotNull(payload);
        Assert.Null(payload!.TemperatureCelsius);
    }

    [Fact]
    public void test_temperature_out_of_range_rejected()
    {
        var data = BuildSoilPressureBytes(45.2f, 90.0f);
        var (err, _) = SoilPressurePayloadDecoder.Decode(data);
        Assert.NotNull(err);
        Assert.Equal(ParseStage.PayloadDecode, err!.Error!.Stage);
    }
}
