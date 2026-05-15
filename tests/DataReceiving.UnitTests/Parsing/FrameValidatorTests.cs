using DataReceiving.Parsing;
using DataReceiving.UnitTests;

namespace DataReceiving.UnitTests.Parsing;

public class FrameValidatorTests
{
    private static readonly byte[] MinimalPayload = new byte[1] { 0xFF };

    private static byte[] ValidFrame(byte[]? payload = null) =>
        FrameBuilder.Build(0x01, payload ?? MinimalPayload);

    [Fact]
    public void test_valid_frame_passes()
    {
        var result = FrameValidator.Validate(ValidFrame());
        Assert.True(result.Success);
        Assert.Null(result.Error);
    }

    [Fact]
    public void test_bad_sync_start_rejected()
    {
        var frame = ValidFrame();
        frame[0] = 0xFF;
        var result = FrameValidator.Validate(frame);
        Assert.False(result.Success);
        Assert.Equal(ParseStage.SyncBytes, result.Error!.Stage);
    }

    [Fact]
    public void test_bad_sync_end_rejected()
    {
        var frame = ValidFrame();
        frame[^1] = 0xFF;
        var result = FrameValidator.Validate(frame);
        Assert.False(result.Success);
        Assert.Equal(ParseStage.SyncBytes, result.Error!.Stage);
    }

    [Fact]
    public void test_unknown_version_rejected()
    {
        var frame = ValidFrame();
        // Patch VER and recompute CRC
        var payload = MinimalPayload;
        var modFrame = FrameBuilder.Build(0x01, payload, ver: 0x02);
        var result = FrameValidator.Validate(modFrame);
        Assert.False(result.Success);
        Assert.Equal(ParseStage.Version, result.Error!.Stage);
    }

    [Fact]
    public void test_unknown_type_rejected()
    {
        var frame = FrameBuilder.Build(0x06, MinimalPayload);
        var result = FrameValidator.Validate(frame);
        Assert.False(result.Success);
        Assert.Equal(ParseStage.SensorType, result.Error!.Stage);
    }

    [Fact]
    public void test_zero_length_rejected()
    {
        // Frame with LEN=0: 10 bytes total (no payload), LEN < min of 1
        var rawFrame = new byte[10];
        rawFrame[0] = 0xAA; rawFrame[1] = 0x55;
        rawFrame[2] = 0x01; rawFrame[3] = 0x01;
        rawFrame[4] = 0x00; rawFrame[5] = 0x00; // LEN=0
        ushort crc = Crc16Ccitt.Compute(rawFrame.AsSpan(2, 4));
        rawFrame[6] = (byte)(crc >> 8); rawFrame[7] = (byte)(crc & 0xFF);
        rawFrame[8] = 0x55; rawFrame[9] = 0xAA; // valid SYNC_END

        var result = FrameValidator.Validate(rawFrame);
        Assert.False(result.Success);
        Assert.Equal(ParseStage.Length, result.Error!.Stage);
    }

    [Fact]
    public void test_length_exceeds_max_rejected()
    {
        // Frame with declared LEN=1025 > 1024, with valid sync bytes
        int declaredLen = 1025;
        var rawFrame = new byte[6 + declaredLen + 4];
        rawFrame[0] = 0xAA; rawFrame[1] = 0x55;
        rawFrame[2] = 0x01; rawFrame[3] = 0x01;
        rawFrame[4] = (byte)(declaredLen >> 8); rawFrame[5] = (byte)(declaredLen & 0xFF);
        // SYNC_END at the end
        rawFrame[^2] = 0x55; rawFrame[^1] = 0xAA;

        var result = FrameValidator.Validate(rawFrame);
        Assert.False(result.Success);
        Assert.Equal(ParseStage.Length, result.Error!.Stage);
    }

    [Fact]
    public void test_crc_mismatch_rejected()
    {
        var frame = FrameBuilder.Build(0x01, MinimalPayload, corruptCrc: true);
        var result = FrameValidator.Validate(frame);
        Assert.False(result.Success);
        Assert.Equal(ParseStage.Crc, result.Error!.Stage);
    }

    [Fact]
    public void test_frame_too_short_for_declared_length()
    {
        // Build a valid frame and then truncate it
        var frame = FrameBuilder.Build(0x01, new byte[10]);
        var truncated = frame[..^3]; // remove last 3 bytes — frame size != expected
        var result = FrameValidator.Validate(truncated);
        Assert.False(result.Success);
        // Could be SyncBytes (too short) or Length (size mismatch)
        Assert.True(result.Error!.Stage is ParseStage.SyncBytes or ParseStage.Length);
    }
}
