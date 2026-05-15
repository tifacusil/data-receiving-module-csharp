using DataReceiving.Parsing.Payloads;
using DataReceiving.Validation;

namespace DataReceiving.Parsing;

public class DtuPacketParser : IDtuPacketParser
{
    private readonly SchemaValidator _validator;

    private static readonly Dictionary<byte, string> TypeToSensorType = new()
    {
        [0x01] = "gnss",
        [0x02] = "rainfall",
        [0x03] = "strain",
        [0x04] = "soil_pressure",
        [0x05] = "deep_displacement",
    };

    public DtuPacketParser(SchemaValidator validator)
    {
        _validator = validator;
    }

    public ParseResult Parse(ReadOnlySpan<byte> rawFrame)
    {
        string receivedAt = DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ssZ");

        // Stage 1: Frame structure + CRC
        var validation = FrameValidator.Validate(rawFrame);
        if (!validation.Success) return validation;

        byte typeCode = rawFrame[3];
        string sensorType = TypeToSensorType[typeCode];

        // Payload is bytes 6..(frame.Length - 4)
        var payload = rawFrame[6..(rawFrame.Length - 4)];

        // Stage 2: Header decode
        var (headerError, headerLen, messageId, projectId, sensorId, timestamp, samplingInterval) =
            EnvelopeDecoder.Decode(payload);
        if (headerError is not null) return headerError;

        var sensorPayloadData = payload[headerLen..];

        // Stage 3: Payload decode
        var (payloadError, sensorPayload) = DecodePayload(sensorType, sensorPayloadData);
        if (payloadError is not null) return payloadError;

        var envelope = new SensorEnvelope
        {
            MessageId = messageId,
            ProjectId = projectId,
            SensorId = sensorId,
            SensorType = sensorType,
            Timestamp = timestamp,
            ReceivedAt = receivedAt,
            SamplingIntervalSeconds = samplingInterval,
            Payload = sensorPayload!,
        };

        // Stage 4: JSON Schema validation
        string json = EnvelopeSerializer.Serialize(envelope);
        var (valid, schemaError) = _validator.Validate(json, sensorType);
        if (!valid)
            return new ParseResult(false, null, new ParseError(ParseStage.SchemaValidation, schemaError ?? "Schema validation failed"));

        return new ParseResult(true, envelope, null);
    }

    private static (ParseResult? Error, object? Payload) DecodePayload(string sensorType, ReadOnlySpan<byte> data)
    {
        switch (sensorType)
        {
            case "gnss":
            {
                var (err, p) = GnssPayloadDecoder.Decode(data);
                return err is not null ? (err, null) : (null, p);
            }
            case "rainfall":
            {
                var (err, p) = RainfallPayloadDecoder.Decode(data);
                return err is not null ? (err, null) : (null, p);
            }
            case "strain":
            {
                var (err, p) = StrainPayloadDecoder.Decode(data);
                return err is not null ? (err, null) : (null, p);
            }
            case "soil_pressure":
            {
                var (err, p) = SoilPressurePayloadDecoder.Decode(data);
                return err is not null ? (err, null) : (null, p);
            }
            case "deep_displacement":
            {
                var (err, p) = DeepDisplacementPayloadDecoder.Decode(data);
                return err is not null ? (err, null) : (null, p);
            }
            default:
                return (new ParseResult(false, null, new ParseError(ParseStage.SensorType, $"Unknown sensor type: {sensorType}")), null);
        }
    }
}
