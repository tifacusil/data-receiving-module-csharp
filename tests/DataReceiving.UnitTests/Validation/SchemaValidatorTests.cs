using DataReceiving.Validation;

namespace DataReceiving.UnitTests.Validation;

public class SchemaValidatorTests
{
    private readonly SchemaValidator _validator = new();

    private static string GnssEnvelope(string? extraField = null) =>
        $$"""
        {
          "schema_version": "1.0",
          "message_id": "550e8400-e29b-41d4-a716-446655440000",
          "project_id": "shangtan",
          "sensor_id": "GXZM-DB03",
          "sensor_type": "gnss",
          "timestamp": "2026-05-02T14:30:00Z",
          "received_at": "2026-05-02T14:30:12Z",
          "sampling_interval_seconds": 3600,
          "payload": {
            "displacement_x_mm": 12.34,
            "displacement_y_mm": -5.67,
            "displacement_z_mm": 2.10,
            "displacement_horizontal_mm": 13.57,
            "cumulative_displacement_mm": 1247.5,
            "fix_quality": "rtk_fixed"
            {{(extraField is not null ? "," + extraField : "")}}
          }
        }
        """;

    [Fact]
    public void test_valid_gnss_envelope_passes()
    {
        var (valid, err) = _validator.Validate(GnssEnvelope(), "gnss");
        Assert.True(valid, err);
    }

    [Fact]
    public void test_valid_rainfall_envelope_passes()
    {
        string json = """
        {
          "schema_version": "1.0",
          "message_id": "7c9e6679-7425-40de-944b-e07fc1f90ae7",
          "project_id": "shangtan",
          "sensor_id": "GXZM-RG01",
          "sensor_type": "rainfall",
          "timestamp": "2026-05-02T14:00:00Z",
          "received_at": "2026-05-02T14:00:08Z",
          "sampling_interval_seconds": 3600,
          "payload": {
            "rainfall_mm": 2.4,
            "cumulative_rainfall_mm": 1547.2,
            "interval_minutes": 60
          }
        }
        """;
        var (valid, err) = _validator.Validate(json, "rainfall");
        Assert.True(valid, err);
    }

    [Fact]
    public void test_valid_strain_envelope_passes()
    {
        string json = """
        {
          "schema_version": "1.0",
          "message_id": "f47ac10b-58cc-4372-a567-0e02b2c3d479",
          "project_id": "shangtan",
          "sensor_id": "GXZM-SG02",
          "sensor_type": "strain",
          "timestamp": "2026-05-02T14:00:00Z",
          "received_at": "2026-05-02T14:00:05Z",
          "sampling_interval_seconds": 3600,
          "payload": {
            "strain_microstrain": 234.5,
            "temperature_celsius": 24.3
          }
        }
        """;
        var (valid, err) = _validator.Validate(json, "strain");
        Assert.True(valid, err);
    }

    [Fact]
    public void test_valid_soil_pressure_envelope_passes()
    {
        string json = """
        {
          "schema_version": "1.0",
          "message_id": "16fd2706-8baf-433b-82eb-8c7fada847da",
          "project_id": "shangtan",
          "sensor_id": "GXZM-SP01",
          "sensor_type": "soil_pressure",
          "timestamp": "2026-05-02T14:00:00Z",
          "received_at": "2026-05-02T14:00:03Z",
          "sampling_interval_seconds": 3600,
          "payload": {
            "pressure_kpa": 45.2,
            "temperature_celsius": 18.7
          }
        }
        """;
        var (valid, err) = _validator.Validate(json, "soil_pressure");
        Assert.True(valid, err);
    }

    [Fact]
    public void test_valid_deep_displacement_envelope_passes()
    {
        string json = """
        {
          "schema_version": "1.0",
          "message_id": "a3bb189e-8bf9-3888-9912-ace4e6543002",
          "project_id": "lingzhan",
          "sensor_id": "LZ-DB01",
          "sensor_type": "deep_displacement",
          "timestamp": "2019-06-15T06:00:00Z",
          "received_at": "2019-06-15T06:00:07Z",
          "sampling_interval_seconds": 3600,
          "payload": {
            "depth_m": 15.0,
            "tilt_x_mm": 3.21,
            "tilt_y_mm": -1.54,
            "cumulative_tilt_mm": 48.7
          }
        }
        """;
        var (valid, err) = _validator.Validate(json, "deep_displacement");
        Assert.True(valid, err);
    }

    [Fact]
    public void test_missing_required_field_fails()
    {
        string json = """
        {
          "schema_version": "1.0",
          "message_id": "550e8400-e29b-41d4-a716-446655440000",
          "project_id": "shangtan",
          "sensor_id": "GXZM-DB03",
          "sensor_type": "gnss",
          "timestamp": "2026-05-02T14:30:00Z",
          "received_at": "2026-05-02T14:30:12Z",
          "sampling_interval_seconds": 3600,
          "payload": {
            "displacement_y_mm": -5.67,
            "displacement_z_mm": 2.10,
            "displacement_horizontal_mm": 13.57,
            "cumulative_displacement_mm": 1247.5,
            "fix_quality": "rtk_fixed"
          }
        }
        """;
        var (valid, _) = _validator.Validate(json, "gnss");
        Assert.False(valid);
    }

    [Fact]
    public void test_additional_property_fails()
    {
        var (valid, _) = _validator.Validate(GnssEnvelope(@"""extra_field"": 99"), "gnss");
        Assert.False(valid);
    }

    [Fact]
    public void test_wrong_project_id_pattern_fails()
    {
        string json = """
        {
          "schema_version": "1.0",
          "message_id": "550e8400-e29b-41d4-a716-446655440000",
          "project_id": "Shangtan",
          "sensor_id": "GXZM-DB03",
          "sensor_type": "gnss",
          "timestamp": "2026-05-02T14:30:00Z",
          "received_at": "2026-05-02T14:30:12Z",
          "sampling_interval_seconds": 3600,
          "payload": {
            "displacement_x_mm": 12.34,
            "displacement_y_mm": -5.67,
            "displacement_z_mm": 2.10,
            "displacement_horizontal_mm": 13.57,
            "cumulative_displacement_mm": 1247.5,
            "fix_quality": "rtk_fixed"
          }
        }
        """;
        var (valid, _) = _validator.Validate(json, "gnss");
        Assert.False(valid);
    }
}
