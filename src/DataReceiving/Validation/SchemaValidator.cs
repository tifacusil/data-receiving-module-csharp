using System.Text.Json;
using System.Text.Json.Nodes;
using Json.Schema;

namespace DataReceiving.Validation;

public class SchemaValidator
{
    private static readonly EvaluationOptions EvalOptions = new() { OutputFormat = OutputFormat.List };

    private readonly JsonSchema _envelopeSchema;
    private readonly IReadOnlyDictionary<string, JsonSchema> _payloadSchemas;

    public SchemaValidator()
    {
        _envelopeSchema = LoadSchema("envelope.schema.json");
        _payloadSchemas = new Dictionary<string, JsonSchema>
        {
            ["gnss"]              = LoadSchema("payload-gnss.schema.json"),
            ["rainfall"]          = LoadSchema("payload-rainfall.schema.json"),
            ["strain"]            = LoadSchema("payload-strain.schema.json"),
            ["soil_pressure"]     = LoadSchema("payload-soil-pressure.schema.json"),
            ["deep_displacement"] = LoadSchema("payload-deep-displacement.schema.json"),
        };
    }

    public (bool Valid, string? ErrorMessage) Validate(string envelopeJson, string sensorType)
    {
        var node = JsonNode.Parse(envelopeJson);
        if (node is null)
            return (false, "Failed to parse envelope JSON");

        var envResult = _envelopeSchema.Evaluate(node, EvalOptions);
        if (!envResult.IsValid)
            return (false, FormatErrors(envResult));

        if (_payloadSchemas.TryGetValue(sensorType, out var payloadSchema))
        {
            var payloadNode = node["payload"];
            if (payloadNode is null)
                return (false, "Missing 'payload' in envelope");

            var payloadResult = payloadSchema.Evaluate(payloadNode, EvalOptions);
            if (!payloadResult.IsValid)
                return (false, FormatErrors(payloadResult));
        }

        return (true, null);
    }

    private static JsonSchema LoadSchema(string resourceName)
    {
        var assembly = typeof(SchemaValidator).Assembly;
        var fullName = assembly.GetManifestResourceNames()
            .FirstOrDefault(n => n.EndsWith(resourceName, StringComparison.OrdinalIgnoreCase))
            ?? throw new InvalidOperationException($"Embedded schema '{resourceName}' not found. Available: {string.Join(", ", assembly.GetManifestResourceNames())}");

        using var stream = assembly.GetManifestResourceStream(fullName)
            ?? throw new InvalidOperationException($"Could not open stream for '{fullName}'");
        using var reader = new StreamReader(stream);
        string json = reader.ReadToEnd();
        return JsonSchema.FromText(json);
    }

    private static string FormatErrors(EvaluationResults result)
    {
        var errors = result.Details
            .Where(d => !d.IsValid && d.Errors?.Count > 0)
            .SelectMany(d => d.Errors!.Select(e => $"{d.InstanceLocation}: {e.Value}"));
        return string.Join("; ", errors);
    }
}
