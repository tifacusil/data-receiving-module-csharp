using System.Buffers.Binary;

namespace DataReceiving.Parsing.Payloads;

public static class SoilPressurePayloadDecoder
{
    public static (ParseResult? Error, SoilPressurePayload? Payload) Decode(ReadOnlySpan<byte> data)
    {
        // HAS_TEMP(1) + pressure(4) = 5 min, + temp(4) = 9 with temperature
        if (data.Length < 5)
            return (Fail("Soil pressure payload too short, need at least 5 bytes"), null);

        byte hasTemp = data[0];
        float pressure = BinaryPrimitives.ReadSingleBigEndian(data[1..5]);

        float? temperature = null;
        if (hasTemp == 0x01)
        {
            if (data.Length < 9)
                return (Fail("Soil pressure payload too short for temperature, need 9 bytes when HAS_TEMP=0x01"), null);
            float temp = BinaryPrimitives.ReadSingleBigEndian(data[5..9]);
            if (temp < -40 || temp > 80)
                return (Fail($"temperature_celsius {temp} out of range [-40..80]"), null);
            temperature = temp;
        }
        else if (hasTemp != 0x00)
        {
            return (Fail($"Invalid HAS_TEMP byte 0x{hasTemp:X2}, expected 0x00 or 0x01"), null);
        }

        return (null, new SoilPressurePayload(pressure, temperature));
    }

    private static ParseResult Fail(string msg) =>
        new(false, null, new ParseError(ParseStage.PayloadDecode, msg));
}
