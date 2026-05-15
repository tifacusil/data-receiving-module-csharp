namespace DataReceiving.Parsing;

public static class FrameValidator
{
    // Minimum parseable frame: SYNC_START(2) + VER(1) + TYPE(1) + LEN(2) + CRC(2) + SYNC_END(2) = 10
    // (LEN=0 passes this check but is rejected by the LEN range validation below)
    private const int MinFrameSize = 10;
    private const int ProtocolVersion = 0x01;
    private const int MaxPayloadLen = 1024;
    private const int MinPayloadLen = 1;

    public static ParseResult Validate(ReadOnlySpan<byte> frame)
    {
        if (frame.Length < MinFrameSize)
            return Fail(ParseStage.SyncBytes, "Frame too short");

        if (frame[0] != 0xAA || frame[1] != 0x55)
            return Fail(ParseStage.SyncBytes, $"Bad sync start: 0x{frame[0]:X2} 0x{frame[1]:X2}");

        int end = frame.Length - 1;
        if (frame[end] != 0xAA || frame[end - 1] != 0x55)
            return Fail(ParseStage.SyncBytes, $"Bad sync end: 0x{frame[end - 1]:X2} 0x{frame[end]:X2}");

        if (frame[2] != ProtocolVersion)
            return Fail(ParseStage.Version, $"Unknown version: 0x{frame[2]:X2}");

        byte typeCode = frame[3];
        if (typeCode < 0x01 || typeCode > 0x05)
            return Fail(ParseStage.SensorType, $"Unknown sensor type: 0x{typeCode:X2}");

        int declaredLen = (frame[4] << 8) | frame[5];
        if (declaredLen < MinPayloadLen || declaredLen > MaxPayloadLen)
            return Fail(ParseStage.Length, $"Payload length {declaredLen} out of range [1..1024]");

        // frame must be: 2(sync) + 1(ver) + 1(type) + 2(len) + declaredLen + 2(crc) + 2(sync) = 10 + declaredLen
        int expectedLen = 10 + declaredLen;
        if (frame.Length != expectedLen)
            return Fail(ParseStage.Length, $"Frame size {frame.Length} != expected {expectedLen} for declared payload length {declaredLen}");

        // CRC covers VER + TYPE + LEN + PAYLOAD (bytes 2..end-4 inclusive)
        var crcData = frame[2..(frame.Length - 4)];
        ushort computed = Crc16Ccitt.Compute(crcData);
        int crcOffset = frame.Length - 4;
        ushort received = (ushort)((frame[crcOffset] << 8) | frame[crcOffset + 1]);
        if (computed != received)
            return Fail(ParseStage.Crc, $"CRC mismatch: expected 0x{computed:X4} got 0x{received:X4}");

        return new ParseResult(true, null, null);
    }

    private static ParseResult Fail(ParseStage stage, string msg) =>
        new(false, null, new ParseError(stage, msg));
}
