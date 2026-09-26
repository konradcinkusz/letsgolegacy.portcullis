using System.Text.Json.Nodes;

namespace Portcullis.Engine.Tests.Sarif;

/// <summary>
/// The validator the SARIF tests rely on, checked before it is trusted: the embedded schema
/// is the official file, reading it as draft-07 is faithful (see <see cref="SarifSchema"/>),
/// and it does reject invalid logs — a validator that accepted everything would make every
/// "the output is valid SARIF" assertion in <see cref="SarifReportTests"/> vacuous.
/// </summary>
public class SarifSchemaTests
{
    // Keywords whose meaning is identical in JSON Schema draft-04 and draft-07.
    private static readonly HashSet<string> SameInBothDrafts =
    [
        "$schema", "$ref", "title", "description", "default", "definitions", "type", "enum", "format",
        "properties", "additionalProperties", "required", "items", "uniqueItems", "minItems", "maxItems",
        "minimum", "maximum", "minLength", "maxLength", "pattern", "patternProperties", "anyOf", "oneOf",
        "allOf", "not", "dependencies", "multipleOf", "minProperties", "maxProperties",
    ];

    private const string MinimalValidLog = """
        {
          "$schema": "https://docs.oasis-open.org/sarif/sarif/v2.1.0/errata01/os/schemas/sarif-schema-2.1.0.json",
          "version": "2.1.0",
          "runs": [
            {
              "tool": { "driver": { "name": "Portcullis" } },
              "results": [
                {
                  "ruleId": "RULE",
                  "message": { "text": "m" },
                  "locations": [
                    {
                      "physicalLocation": {
                        "artifactLocation": { "uri": "src/My%20File.cs", "uriBaseId": "%SRCROOT%" },
                        "region": { "startLine": 3 }
                      }
                    }
                  ]
                }
              ]
            }
          ]
        }
        """;

    [Fact]
    public void EmbeddedSchema_IsTheOfficialFileUnchanged()
    {
        Assert.Equal(SarifSchema.Sha256, SarifSchema.ComputeSha256());

        var schema = JsonNode.Parse(SarifSchema.Text())!;
        Assert.Equal(
            "https://docs.oasis-open.org/sarif/sarif/v2.1.0/errata01/os/schemas/sarif-schema-2.1.0.json",
            (string?)schema["id"]);
        Assert.Equal("http://json-schema.org/draft-04/schema#", (string?)schema["$schema"]);
    }

    [Fact]
    public void EmbeddedSchema_UsesOnlyKeywordsThatMeanTheSameAsDraft07_AndOnlyLocalReferences()
    {
        var keywords = new HashSet<string>(StringComparer.Ordinal);
        var references = new List<string>();
        Collect(JsonNode.Parse(SarifSchema.Text()), keywords, references);

        // `id` is draft-04's name for draft-06's `$id`. Under draft-07 it is ignored, which is
        // harmless only because no reference resolves against it:
        Assert.All(references, r => Assert.StartsWith("#/definitions/", r));
        Assert.Empty(keywords.Except(SameInBothDrafts).Except(new[] { "id" }));
    }

    [Fact]
    public void Validator_AcceptsAMinimalValidLog()
    {
        Assert.Empty(SarifSchema.Validate(MinimalValidLog));
    }

    [Theory]
    [InlineData("\"version\": \"2.1.0\"", "\"version\": \"2.0.0\"")]
    [InlineData("\"message\": { \"text\": \"m\" },", "")]
    [InlineData("\"startLine\": 3", "\"startLine\": 0")]
    [InlineData("\"tool\": { \"driver\": { \"name\": \"Portcullis\" } }", "\"tool\": { }")]
    [InlineData("src/My%20File.cs", "src/My File.cs")]
    [InlineData("\"ruleId\": \"RULE\",", "\"ruleId\": \"RULE\", \"level\": \"fatal\",")]
    public void Validator_RejectsALogBrokenInAnyOfTheseWays(string valid, string broken)
    {
        // The last-but-one case is a format assertion: a space is not allowed in a URI
        // reference, and only RequireFormatValidation makes the schema's "format" bite.
        Assert.Contains(valid, MinimalValidLog);

        Assert.NotEmpty(SarifSchema.Validate(MinimalValidLog.Replace(valid, broken)));
    }

    // Collects every keyword used anywhere in the schema. Property and definition names are
    // names, not keywords, and enum, default and required hold values, not schemas.
    private static void Collect(JsonNode? node, HashSet<string> keywords, List<string> references)
    {
        switch (node)
        {
            case JsonObject schema:
                foreach (var (keyword, value) in schema)
                {
                    keywords.Add(keyword);
                    if (keyword == "$ref")
                    {
                        references.Add((string)value!);
                    }
                    else if (keyword is "properties" or "definitions" or "patternProperties" && value is JsonObject named)
                    {
                        foreach (var (_, subschema) in named)
                        {
                            Collect(subschema, keywords, references);
                        }
                    }
                    else if (keyword is not ("enum" or "default" or "required"))
                    {
                        Collect(value, keywords, references);
                    }
                }

                break;
            case JsonArray schemas:
                foreach (var item in schemas)
                {
                    Collect(item, keywords, references);
                }

                break;
        }
    }
}
