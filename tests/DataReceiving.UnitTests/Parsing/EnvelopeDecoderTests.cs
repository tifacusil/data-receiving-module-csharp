using System.Buffers.Binary;
using System.Text;
using DataReceiving.Parsing;
using DataReceiving.UnitTests;

namespace DataReceiving.UnitTests.Parsing;

public class EnvelopeDecoderTests
{
    private static readonly Guid TestGuid = new("550e8400-e29b-41d4-a716-446655440000");
    private const string ProjectId = "shangtan";
    private const string SensorId = "GXZM-DB03";
    private const long TimestampMs = 1777732200000L; // 2026-05-02T14:30:00Z
    private const uint SamplingInterval = 3600;

    private static byte[] BuildValidHeader(
        Guid? guid = null, string? project = null, string? sensor = null,
        long? ts = null, uint? interval = null)
    {
        return FrameBuilder.BuildHeader(
            guid ?? TestGuid,
            project ?? ProjectId,
            sensor ?? SensorId,
            ts ?? TimestampMs,
            interval ?? SamplingInterval);
    }

    [Fact]
    public void test_uuid_decoded_as_canonical_string()
    {
        var header = BuildValidHeader();
        var (err, _, messageId, _, _, _, _) = EnvelopeDecoder.Decode(header.AsSpan());
        Assert.Null(err);
        Assert.Equal("550e8400-e29b-41d4-a716-446655440000", messageId);
    }

    [Fact]
    public void test_project_id_decoded_correctly()
    {
        var header = BuildValidHeader();
        var (err, _, _, projectId, _, _, _) = EnvelopeDecoder.Decode(header.AsSpan());
        Assert.Null(err);
        Assert.Equal("shangtan", projectId);
    }

    [Fact]
    public void test_sensor_id_decoded_correctly()
    {
        var header = BuildValidHeader();
        var (err, _, _, _, sensorId, _, _) = EnvelopeDecoder.Decode(header.AsSpan());
        Assert.Null(err);
        Assert.Equal("GXZM-DB03", sensorId);
    }

    [Fact]
    public void test_timestamp_epoch_ms_to_iso8601()
    {
        var header = BuildValidHeader(ts: 1777732200000L);
        var (err, _, _, _, _, timestamp, _) = EnvelopeDecoder.Decode(header.AsSpan());
        Assert.Null(err);
        Assert.Equal("2026-05-02T14:30:00Z", timestamp);
    }

    [Fact]
    public void test_sampling_interval_decoded()
    {
        var header = BuildValidHeader(interval: 3600);
        var (err, _, _, _, _, _, samplingInterval) = EnvelopeDecoder.Decode(header.AsSpan());
        Assert.Null(err);
        Assert.Equal(3600, samplingInterval);
    }

    [Fact]
    public void test_invalid_project_id_pattern_rejected()
    {
        var header = BuildValidHeader(project: "Shangtan"); // uppercase: invalid
        var (err, _, _, _, _, _, _) = EnvelopeDecoder.Decode(header.AsSpan());
        Assert.NotNull(err);
        Assert.Equal(ParseStage.HeaderDecode, err!.Error!.Stage);
    }

    [Fact]
    public void test_proj_len_out_of_range_rejected()
    {
        // Build header with 2-char project_id (too short: min is 3)
        var header = BuildValidHeader(project: "ab");
        var (err, _, _, _, _, _, _) = EnvelopeDecoder.Decode(header.AsSpan());
        Assert.NotNull(err);
        Assert.Equal(ParseStage.HeaderDecode, err!.Error!.Stage);
    }

    [Fact]
    public void test_sens_len_out_of_range_rejected()
    {
        // Build header with empty sensor_id (length 0: invalid)
        var buf = new List<byte>();
        buf.AddRange(TestGuid.ToByteArray(bigEndian: true));
        byte[] projBytes = Encoding.ASCII.GetBytes("shangtan");
        buf.Add((byte)projBytes.Length);
        buf.AddRange(projBytes);
        buf.Add(0); // SENS_LEN = 0: invalid

        var header = buf.ToArray();
        var (err, _, _, _, _, _, _) = EnvelopeDecoder.Decode(header.AsSpan());
        Assert.NotNull(err);
        Assert.Equal(ParseStage.HeaderDecode, err!.Error!.Stage);
    }
}
