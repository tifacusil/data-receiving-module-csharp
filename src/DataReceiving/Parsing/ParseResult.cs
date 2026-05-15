namespace DataReceiving.Parsing;

public enum ParseStage
{
    SyncBytes,
    Version,
    SensorType,
    Length,
    Crc,
    HeaderDecode,
    PayloadDecode,
    SchemaValidation
}

public record ParseError(ParseStage Stage, string Message);

public record ParseResult(bool Success, SensorEnvelope? Envelope, ParseError? Error);
