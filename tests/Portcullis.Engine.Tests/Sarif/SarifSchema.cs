using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Json.Schema;

namespace Portcullis.Engine.Tests.Sarif;

/// <summary>
/// The official OASIS SARIF 2.1.0 schema, embedded in this assembly (see
/// THIRD-PARTY-NOTICE.md beside it), and a validator for it.
///
/// The schema declares JSON Schema draft-04, which JsonSchema.Net does not implement, so it
/// is evaluated as draft-07. That is a faithful reading of this particular schema, not an
/// approximation, and <see cref="SarifSchemaTests"/> keeps it so: it asserts that the schema
/// uses only keywords whose meaning is the same in both drafts, plus draft-04's <c>id</c>,
/// which draft-07 ignores and which nothing here depends on because every <c>$ref</c> in
/// the schema is local ("#/definitions/..."). The keywords draft-06 changed —
/// <c>exclusiveMinimum</c> and <c>exclusiveMaximum</c> went from booleans to numbers — do
/// not occur in it.
///
/// Format assertions are switched on: JSON Schema treats <c>format</c> as an annotation by
/// default, and without this a result whose location is not a valid URI reference would
/// pass.
/// </summary>
internal static class SarifSchema
{
    public const string ResourceName = "sarif-schema-2.1.0.json";

    /// <summary>SHA-256 of the file as published upstream (LF line endings).</summary>
    public const string Sha256 = "c3b4bb2d6093897483348925aaa73af03b3e3f4bd4ca38cef26dcb4212a2682e";

    private static readonly Lazy<JsonSchema> Schema = new(() =>
    {
        var document = JsonNode.Parse(Text())!.AsObject();
        document["$schema"] = "http://json-schema.org/draft-07/schema#";
        return JsonSchema.FromText(document.ToJsonString());
    });

    /// <summary>The embedded schema, with line endings normalised to LF.</summary>
    public static string Text()
    {
        using var stream = typeof(SarifSchema).Assembly.GetManifestResourceStream(ResourceName)
            ?? throw new InvalidOperationException($"The embedded resource '{ResourceName}' is missing.");
        using var reader = new StreamReader(stream, Encoding.UTF8);
        return reader.ReadToEnd().Replace("\r\n", "\n");
    }

    public static string ComputeSha256() =>
        Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(Text())));

    /// <summary>Every schema violation in <paramref name="sarif"/>, one line each; empty when it is valid.</summary>
    public static IReadOnlyList<string> Validate(string sarif)
    {
        using var instance = JsonDocument.Parse(sarif);
        var results = Schema.Value.Evaluate(
            instance.RootElement,
            new EvaluationOptions { OutputFormat = OutputFormat.List, RequireFormatValidation = true });
        if (results.IsValid)
        {
            return [];
        }

        return new[] { results }
            .Concat(results.Details ?? [])
            .Where(r => r.Errors is { Count: > 0 })
            .SelectMany(r => r.Errors!.Select(e => $"{r.InstanceLocation}: {e.Key}: {e.Value}"))
            .DefaultIfEmpty("invalid, with no error detail reported")
            .ToList();
    }
}
