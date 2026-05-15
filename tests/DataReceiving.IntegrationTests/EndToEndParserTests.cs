using System.Buffers.Binary;
using System.Text.Json;
using DataReceiving.Parsing;
using DataReceiving.Validation;

namespace DataReceiving.IntegrationTests;

public class EndToEndParserTests
{
    private readonly DtuPacketParser _parser = new(new SchemaValidator());

    // gnss-shangtan example values
    private static readonly Guid GnssGuid = new("550e8400-e29b-41d4-a716-446655440000");
    private const string GnssProject = "shangtan";
    private const string GnssSensor = "GXZM-DB03";
    private const long GnssTs = 1777732200000L; // 2026-05-02T14:30:00Z
    private const uint GnssInterval = 3600;

    private static byte[] GnssPayloadBytes(float dispX, float dispY, float dispZ, float dispH, float cumul, byte fix)
    {
        var buf = new byte[21];
        BinaryPrimitives.WriteSingleBigEndian(buf.AsSpan(0, 4), dispX);
        BinaryPrimitives.WriteSingleBigEndian(buf.AsSpan(4, 4), dispY);
        BinaryPrimitives.WriteSingleBigEndian(buf.AsSpan(8, 4), dispZ);
        BinaryPrimitives.WriteSingleBigEndian(buf.AsSpan(12, 4), dispH);
        BinaryPrimitives.WriteSingleBigEndian(buf.AsSpan(16, 4), cumul);
        buf[20] = fix;
        return buf;
    }

    private static byte[] BuildGnssFrame(bool corruptCrc = false)
    {
        var header = FrameBuilder.BuildHeader(GnssGuid, GnssProject, GnssSensor, GnssTs, GnssInterval);
        var gnssData = GnssPayloadBytes(12.34f, -5.67f, 2.10f, 13.57f, 1247.5f, 0x04);
        var payload = header.Concat(gnssData).ToArray();
        return FrameBuilder.Build(0x01, payload, corruptCrc);
    }

    [Fact]
    public void test_round_trip_gnss()
    {
        var frame = BuildGnssFrame();
        var result = _parser.Parse(frame);

        Assert.True(result.Success, result.Error?.Message);
        Assert.NotNull(result.Envelope);
        var env = result.Envelope!;
        Assert.Equal("1.0", env.SchemaVersion);
        Assert.Equal("550e8400-e29b-41d4-a716-446655440000", env.MessageId);
        Assert.Equal("shangtan", env.ProjectId);
        Assert.Equal("GXZM-DB03", env.SensorId);
        Assert.Equal("gnss", env.SensorType);
        Assert.Equal("2026-05-02T14:30:00Z", env.Timestamp);
        Assert.Equal(3600, env.SamplingIntervalSeconds);

        var gnss = Assert.IsType<GnssPayload>(env.Payload);
        Assert.Equal(12.34f, gnss.DisplacementXMm, precision: 2);
        Assert.Equal(-5.67f, gnss.DisplacementYMm, precision: 2);
        Assert.Equal(2.10f, gnss.DisplacementZMm, precision: 2);
        Assert.Equal(13.57f, gnss.DisplacementHorizontalMm, precision: 2);
        Assert.Equal(1247.5f, gnss.CumulativeDisplacementMm, precision: 1);
        Assert.Equal("rtk_fixed", gnss.FixQuality);
    }

    [Fact]
    public void test_round_trip_rainfall()
    {
        var header = FrameBuilder.BuildHeader(
            new Guid("7c9e6679-7425-40de-944b-e07fc1f90ae7"), "shangtan", "GXZM-RG01",
            1746194400000L, // 2026-05-02T14:00:00Z
            3600);

        var rain = new byte[10];
        BinaryPrimitives.WriteSingleBigEndian(rain.AsSpan(0, 4), 2.4f);
        BinaryPrimitives.WriteSingleBigEndian(rain.AsSpan(4, 4), 1547.2f);
        BinaryPrimitives.WriteUInt16BigEndian(rain.AsSpan(8, 2), 60);

        var payload = header.Concat(rain).ToArray();
        var frame = FrameBuilder.Build(0x02, payload);
        var result = _parser.Parse(frame);

        Assert.True(result.Success, result.Error?.Message);
        var r = Assert.IsType<RainfallPayload>(result.Envelope!.Payload);
        Assert.Equal(2.4f, r.RainfallMm, precision: 2);
        Assert.Equal(1547.2f, r.CumulativeRainfallMm, precision: 1);
        Assert.Equal(60, r.IntervalMinutes);
    }

    [Fact]
    public void test_round_trip_strain_with_temp()
    {
        var header = FrameBuilder.BuildHeader(
            new Guid("f47ac10b-58cc-4372-a567-0e02b2c3d479"), "shangtan", "GXZM-SG02",
            1746194400000L, 3600);

        var strain = new byte[9];
        strain[0] = 0x01;
        BinaryPrimitives.WriteSingleBigEndian(strain.AsSpan(1, 4), 234.5f);
        BinaryPrimitives.WriteSingleBigEndian(strain.AsSpan(5, 4), 24.3f);

        var payload = header.Concat(strain).ToArray();
        var frame = FrameBuilder.Build(0x03, payload);
        var result = _parser.Parse(frame);

        Assert.True(result.Success, result.Error?.Message);
        var s = Assert.IsType<StrainPayload>(result.Envelope!.Payload);
        Assert.Equal(234.5f, s.StrainMicrostrain, precision: 1);
        Assert.NotNull(s.TemperatureCelsius);
        Assert.Equal(24.3f, s.TemperatureCelsius!.Value, precision: 1);
    }

    [Fact]
    public void test_round_trip_strain_without_temp()
    {
        var header = FrameBuilder.BuildHeader(
            new Guid("f47ac10b-58cc-4372-a567-0e02b2c3d479"), "shangtan", "GXZM-SG02",
            1746194400000L, 3600);

        var strain = new byte[5];
        strain[0] = 0x00;
        BinaryPrimitives.WriteSingleBigEndian(strain.AsSpan(1, 4), 234.5f);

        var payload = header.Concat(strain).ToArray();
        var frame = FrameBuilder.Build(0x03, payload);
        var result = _parser.Parse(frame);

        Assert.True(result.Success, result.Error?.Message);
        var s = Assert.IsType<StrainPayload>(result.Envelope!.Payload);
        Assert.Null(s.TemperatureCelsius);

        // JSON output must NOT include temperature_celsius
        string json = EnvelopeSerializer.Serialize(result.Envelope!);
        using var doc = JsonDocument.Parse(json);
        Assert.False(doc.RootElement.GetProperty("payload").TryGetProperty("temperature_celsius", out _));
    }

    [Fact]
    public void test_round_trip_soil_pressure_with_temp()
    {
        var header = FrameBuilder.BuildHeader(
            new Guid("16fd2706-8baf-433b-82eb-8c7fada847da"), "shangtan", "GXZM-SP01",
            1746194400000L, 3600);

        var soil = new byte[9];
        soil[0] = 0x01;
        BinaryPrimitives.WriteSingleBigEndian(soil.AsSpan(1, 4), 45.2f);
        BinaryPrimitives.WriteSingleBigEndian(soil.AsSpan(5, 4), 18.7f);

        var payload = header.Concat(soil).ToArray();
        var frame = FrameBuilder.Build(0x04, payload);
        var result = _parser.Parse(frame);

        Assert.True(result.Success, result.Error?.Message);
        var sp = Assert.IsType<SoilPressurePayload>(result.Envelope!.Payload);
        Assert.Equal(45.2f, sp.PressureKpa, precision: 1);
        Assert.Equal(18.7f, sp.TemperatureCelsius!.Value, precision: 1);
    }

    [Fact]
    public void test_round_trip_deep_displacement()
    {
        var header = FrameBuilder.BuildHeader(
            new Guid("a3bb189e-8bf9-3888-9912-ace4e6543002"), "lingzhan", "LZ-DB01",
            1560578400000L, // 2019-06-15T06:00:00Z
            3600);

        var dd = new byte[16];
        BinaryPrimitives.WriteSingleBigEndian(dd.AsSpan(0, 4), 15.0f);
        BinaryPrimitives.WriteSingleBigEndian(dd.AsSpan(4, 4), 3.21f);
        BinaryPrimitives.WriteSingleBigEndian(dd.AsSpan(8, 4), -1.54f);
        BinaryPrimitives.WriteSingleBigEndian(dd.AsSpan(12, 4), 48.7f);

        var payload = header.Concat(dd).ToArray();
        var frame = FrameBuilder.Build(0x05, payload);
        var result = _parser.Parse(frame);

        Assert.True(result.Success, result.Error?.Message);
        var d = Assert.IsType<DeepDisplacementPayload>(result.Envelope!.Payload);
        Assert.Equal(15.0f, d.DepthM, precision: 1);
        Assert.Equal(3.21f, d.TiltXMm, precision: 2);
        Assert.Equal(-1.54f, d.TiltYMm, precision: 2);
        Assert.Equal(48.7f, d.CumulativeTiltMm, precision: 1);
    }

    [Fact]
    public void test_corrupt_crc_goes_to_dlx()
    {
        var frame = BuildGnssFrame(corruptCrc: true);
        var result = _parser.Parse(frame);
        Assert.False(result.Success);
        Assert.Equal(ParseStage.Crc, result.Error!.Stage);
    }

    [Fact]
    public void test_unknown_type_goes_to_dlx()
    {
        var header = FrameBuilder.BuildHeader(GnssGuid, GnssProject, GnssSensor, GnssTs, GnssInterval);
        var gnssData = GnssPayloadBytes(12.34f, -5.67f, 2.10f, 13.57f, 1247.5f, 0x04);
        var payload = header.Concat(gnssData).ToArray();
        var frame = FrameBuilder.Build(0xFF, payload); // invalid type
        var result = _parser.Parse(frame);
        Assert.False(result.Success);
        Assert.Equal(ParseStage.SensorType, result.Error!.Stage);
    }

    [Fact]
    public void test_schema_violation_goes_to_dlx()
    {
        // GNSS with displacement_horizontal_mm = -1.5 (schema requires >= 0)
        var header = FrameBuilder.BuildHeader(GnssGuid, GnssProject, GnssSensor, GnssTs, GnssInterval);
        var gnssData = GnssPayloadBytes(12.34f, -5.67f, 2.10f, -1.5f, 1247.5f, 0x04);
        var payload = header.Concat(gnssData).ToArray();
        var frame = FrameBuilder.Build(0x01, payload);
        var result = _parser.Parse(frame);
        Assert.False(result.Success);
        // The decoder catches negative horizontal before schema, so it's PayloadDecode
        Assert.True(result.Error!.Stage is ParseStage.PayloadDecode or ParseStage.SchemaValidation);
    }

    [Fact]
    public void test_consecutive_frames_all_parsed()
    {
        var frames = new[]
        {
            BuildGnssFrame(),
            BuildRainfallFrameSimple(),
            BuildStrainFrameSimple(),
            BuildSoilPressureFrameSimple(),
            BuildDeepDispFrameSimple(),
        };

        foreach (var (frame, i) in frames.Select((f, i) => (f, i)))
        {
            var result = _parser.Parse(frame);
            Assert.True(result.Success, $"Frame {i} failed: {result.Error?.Message}");
        }
    }

    private static byte[] BuildRainfallFrameSimple()
    {
        var header = FrameBuilder.BuildHeader(Guid.NewGuid(), "shangtan", "RG01", DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(), 3600);
        var rain = new byte[10];
        BinaryPrimitives.WriteSingleBigEndian(rain.AsSpan(0, 4), 1.0f);
        BinaryPrimitives.WriteSingleBigEndian(rain.AsSpan(4, 4), 100.0f);
        BinaryPrimitives.WriteUInt16BigEndian(rain.AsSpan(8, 2), 60);
        return FrameBuilder.Build(0x02, header.Concat(rain).ToArray());
    }

    private static byte[] BuildStrainFrameSimple()
    {
        var header = FrameBuilder.BuildHeader(Guid.NewGuid(), "shangtan", "SG01", DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(), 3600);
        var strain = new byte[5];
        strain[0] = 0x00;
        BinaryPrimitives.WriteSingleBigEndian(strain.AsSpan(1, 4), 100.0f);
        return FrameBuilder.Build(0x03, header.Concat(strain).ToArray());
    }

    private static byte[] BuildSoilPressureFrameSimple()
    {
        var header = FrameBuilder.BuildHeader(Guid.NewGuid(), "shangtan", "SP01", DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(), 3600);
        var soil = new byte[5];
        soil[0] = 0x00;
        BinaryPrimitives.WriteSingleBigEndian(soil.AsSpan(1, 4), 30.0f);
        return FrameBuilder.Build(0x04, header.Concat(soil).ToArray());
    }

    private static byte[] BuildDeepDispFrameSimple()
    {
        var header = FrameBuilder.BuildHeader(Guid.NewGuid(), "lingzhan", "LZ-DB01", DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(), 3600);
        var dd = new byte[16];
        BinaryPrimitives.WriteSingleBigEndian(dd.AsSpan(0, 4), 5.0f);
        BinaryPrimitives.WriteSingleBigEndian(dd.AsSpan(4, 4), 1.0f);
        BinaryPrimitives.WriteSingleBigEndian(dd.AsSpan(8, 4), -0.5f);
        BinaryPrimitives.WriteSingleBigEndian(dd.AsSpan(12, 4), 10.0f);
        return FrameBuilder.Build(0x05, header.Concat(dd).ToArray());
    }
}
