namespace DataReceiving.Parsing;

public record SensorEnvelope
{
    public string SchemaVersion { get; init; } = "1.0";
    public string MessageId { get; init; } = default!;
    public string ProjectId { get; init; } = default!;
    public string SensorId { get; init; } = default!;
    public string SensorType { get; init; } = default!;
    public string Timestamp { get; init; } = default!;
    public string ReceivedAt { get; init; } = default!;
    public int SamplingIntervalSeconds { get; init; }
    public object Payload { get; init; } = default!;
}

public record GnssPayload(
    float DisplacementXMm,
    float DisplacementYMm,
    float DisplacementZMm,
    float DisplacementHorizontalMm,
    float CumulativeDisplacementMm,
    string FixQuality
);

public record RainfallPayload(
    float RainfallMm,
    float CumulativeRainfallMm,
    int IntervalMinutes
);

public record StrainPayload(
    float StrainMicrostrain,
    float? TemperatureCelsius
);

public record SoilPressurePayload(
    float PressureKpa,
    float? TemperatureCelsius
);

public record DeepDisplacementPayload(
    float DepthM,
    float TiltXMm,
    float TiltYMm,
    float CumulativeTiltMm
);
