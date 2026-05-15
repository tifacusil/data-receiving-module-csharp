using System.Text.Json;
using System.Text.Json.Serialization;

namespace DataReceiving.Parsing;

public static class EnvelopeSerializer
{
    private static readonly JsonSerializerOptions Options = BuildOptions();

    private static JsonSerializerOptions BuildOptions()
    {
        var opts = new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
            WriteIndented = false,
        };
        opts.Converters.Add(new RoundedFloatConverter());
        opts.Converters.Add(new NullableRoundedFloatConverter());
        return opts;
    }

    public static string Serialize(SensorEnvelope envelope)
    {
        var dto = new EnvelopeDto(envelope);
        return JsonSerializer.Serialize(dto, Options);
    }

    // Flat DTO so System.Text.Json can serialize the polymorphic payload properly.
    private class EnvelopeDto
    {
        public string SchemaVersion { get; }
        public string MessageId { get; }
        public string ProjectId { get; }
        public string SensorId { get; }
        public string SensorType { get; }
        public string Timestamp { get; }
        public string ReceivedAt { get; }
        public int SamplingIntervalSeconds { get; }
        public object Payload { get; }

        public EnvelopeDto(SensorEnvelope e)
        {
            SchemaVersion = e.SchemaVersion;
            MessageId = e.MessageId;
            ProjectId = e.ProjectId;
            SensorId = e.SensorId;
            SensorType = e.SensorType;
            Timestamp = e.Timestamp;
            ReceivedAt = e.ReceivedAt;
            SamplingIntervalSeconds = e.SamplingIntervalSeconds;
            Payload = MapPayload(e.Payload);
        }

        private static object MapPayload(object payload) => payload switch
        {
            GnssPayload g => new GnssPayloadDto(g),
            RainfallPayload r => new RainfallPayloadDto(r),
            StrainPayload s => new StrainPayloadDto(s),
            SoilPressurePayload sp => new SoilPressurePayloadDto(sp),
            DeepDisplacementPayload d => new DeepDisplacementPayloadDto(d),
            _ => payload
        };
    }

    private record GnssPayloadDto(float DisplacementXMm, float DisplacementYMm, float DisplacementZMm,
        float DisplacementHorizontalMm, float CumulativeDisplacementMm, string FixQuality)
    {
        public GnssPayloadDto(GnssPayload p) : this(p.DisplacementXMm, p.DisplacementYMm, p.DisplacementZMm,
            p.DisplacementHorizontalMm, p.CumulativeDisplacementMm, p.FixQuality) { }
    }

    private record RainfallPayloadDto(float RainfallMm, float CumulativeRainfallMm, int IntervalMinutes)
    {
        public RainfallPayloadDto(RainfallPayload p) : this(p.RainfallMm, p.CumulativeRainfallMm, p.IntervalMinutes) { }
    }

    private record StrainPayloadDto
    {
        public float StrainMicrostrain { get; }
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public float? TemperatureCelsius { get; }

        public StrainPayloadDto(StrainPayload p)
        {
            StrainMicrostrain = p.StrainMicrostrain;
            TemperatureCelsius = p.TemperatureCelsius;
        }
    }

    private record SoilPressurePayloadDto
    {
        public float PressureKpa { get; }
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public float? TemperatureCelsius { get; }

        public SoilPressurePayloadDto(SoilPressurePayload p)
        {
            PressureKpa = p.PressureKpa;
            TemperatureCelsius = p.TemperatureCelsius;
        }
    }

    private record DeepDisplacementPayloadDto(float DepthM, float TiltXMm, float TiltYMm, float CumulativeTiltMm)
    {
        public DeepDisplacementPayloadDto(DeepDisplacementPayload p)
            : this(p.DepthM, p.TiltXMm, p.TiltYMm, p.CumulativeTiltMm) { }
    }

    private class RoundedFloatConverter : JsonConverter<float>
    {
        public override float Read(ref Utf8JsonReader reader, Type type, JsonSerializerOptions opts) =>
            reader.GetSingle();

        public override void Write(Utf8JsonWriter writer, float value, JsonSerializerOptions opts) =>
            writer.WriteNumberValue((decimal)Math.Round(value, 4));
    }

    private class NullableRoundedFloatConverter : JsonConverter<float?>
    {
        public override float? Read(ref Utf8JsonReader reader, Type type, JsonSerializerOptions opts) =>
            reader.TokenType == JsonTokenType.Null ? null : reader.GetSingle();

        public override void Write(Utf8JsonWriter writer, float? value, JsonSerializerOptions opts)
        {
            if (value is null) writer.WriteNullValue();
            else writer.WriteNumberValue((decimal)Math.Round(value.Value, 4));
        }
    }
}
