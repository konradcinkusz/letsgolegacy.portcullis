using System.Globalization;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.CodeAnalysis;
using Portcullis.Engine.Findings;
using Portcullis.Engine.Model;
using Portcullis.Engine.Provenance;
using Portcullis.Rules;

namespace Portcullis.Engine.Sarif;

/// <summary>
/// A scan as a SARIF 2.1.0 log (docs/SARIF.md): one run, one result per finding that counts
/// against the change being judged.
///
/// "Counts against" is the gate's own definition, applied to every severity rather than only
/// to errors: a finding is left out when it is outside the changed lines of a diff-scoped scan
/// (<see cref="ChangedLines"/>, the same object the gate asks) or when the scan's baseline
/// accepted it. What is left is what a pull request introduced, which is what a code-scanning
/// view of that pull request should show. Nothing leaves silently: the run's
/// <c>properties</c> count the findings each filter removed, and a diff scope that git could
/// not provide is reported as a tool notification alongside results that then cover the whole
/// scan — the same fail-safe fallback the gate takes (docs/DIFF-GATE.md section 3).
///
/// Every result carries a rule id and index, a level, the message, a location relative to the
/// repository root (<c>%SRCROOT%</c>) with the start line, and the finding's
/// <see cref="FindingFingerprint"/> as a partial fingerprint. The output is validated against
/// the official OASIS schema in the test suite.
/// </summary>
public static class SarifReport
{
    /// <summary>The SARIF version written.</summary>
    public const string Version = "2.1.0";

    /// <summary>The official OASIS schema for <see cref="Version"/>, named in every log's <c>$schema</c>.</summary>
    public const string SchemaUri =
        "https://docs.oasis-open.org/sarif/sarif/v2.1.0/errata01/os/schemas/sarif-schema-2.1.0.json";

    /// <summary>The base id every result's location is relative to: the repository root.</summary>
    public const string SourceRootBaseId = "%SRCROOT%";

    /// <summary>Where the tool is documented, as SARIF's <c>informationUri</c>.</summary>
    public const string InformationUri = "https://github.com/konradcinkusz/letsgolegacy.portcullis";

    private static readonly JsonSerializerOptions WriteOptions = new()
    {
        WriteIndented = true,
        NewLine = "\n",
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    /// <summary>The SARIF log for <paramref name="result"/>, as JSON text.</summary>
    /// <inheritdoc cref="Build"/>
    public static string Serialize(ScanResult result, string sourceRoot, ChangedLines? changedLines = null) =>
        Build(result, sourceRoot, changedLines).ToJsonString(WriteOptions) + "\n";

    /// <summary>The SARIF log for <paramref name="result"/>.</summary>
    /// <param name="result">A fresh scan.</param>
    /// <param name="sourceRoot">
    /// The repository root, which result locations are written relative to — the directory a
    /// SARIF consumer such as GitHub code scanning resolves them against. When the scan ran on
    /// a subdirectory, this is the directory above it, not the scanned path.
    /// </param>
    /// <param name="changedLines">
    /// The changed lines of the diff the scan was scoped to, to keep only the findings on
    /// them; null for a scan with no diff scope, or one whose diff scope degraded to the whole
    /// scan.
    /// </param>
    public static JsonObject Build(ScanResult result, string sourceRoot, ChangedLines? changedLines = null)
    {
        var inScope = changedLines is null
            ? result.Violations.ToList()
            : result.Violations.Where(v => changedLines.Contains(v, result.ScannedPath)).ToList();
        var reported = inScope.Where(v => !v.Baselined).ToList();

        var rules = RulesFor(reported);
        var ruleIndex = new Dictionary<string, int>(StringComparer.Ordinal);
        for (var i = 0; i < rules.Count; i++)
        {
            ruleIndex[rules[i].Id] = i;
        }

        var results = new JsonArray();
        foreach (var violation in reported)
        {
            results.Add(ResultFor(
                violation, ruleIndex[violation.RuleId], result.ScannedPath, sourceRoot, result.Baseline is not null));
        }

        var run = new JsonObject
        {
            ["tool"] = new JsonObject
            {
                ["driver"] = new JsonObject
                {
                    ["name"] = "Portcullis",
                    ["version"] = result.EngineVersion,
                    ["informationUri"] = InformationUri,
                    ["rules"] = new JsonArray(rules.Select(RuleJson).ToArray<JsonNode?>()),
                },
            },
            ["invocations"] = new JsonArray(InvocationFor(result)),
            ["originalUriBaseIds"] = new JsonObject
            {
                [SourceRootBaseId] = new JsonObject
                {
                    ["uri"] = DirectoryUri(sourceRoot),
                    ["description"] = Text("The repository root. Result locations are relative to it."),
                },
            },
            ["results"] = results,
            ["properties"] = new JsonObject
            {
                ["scope"] = changedLines is null ? "all" : "diff",
                ["findingsInScan"] = result.Violations.Count,
                ["excludedOutsideChangedLines"] = result.Violations.Count - inScope.Count,
                ["excludedByBaseline"] = inScope.Count - reported.Count,
            },
        };

        return new JsonObject
        {
            ["$schema"] = SchemaUri,
            ["version"] = Version,
            ["runs"] = new JsonArray(run),
        };
    }

    private sealed record Rule(string Id, string Title, string Category, string Level, string? HelpUri);

    // Every registered rule, not only the ones with results: the log then records what was
    // checked as well as what was found, and a consumer can show a rule's description for
    // a result without a second source. Sorted by id so the rule indices are stable.
    private static List<Rule> RulesFor(IReadOnlyList<Violation> reported)
    {
        var rules = new Dictionary<string, Rule>(StringComparer.Ordinal);
        foreach (var descriptor in RuleRegistry.All.SelectMany(a => a.SupportedDiagnostics))
        {
            rules[descriptor.Id] = new Rule(
                descriptor.Id,
                descriptor.Title.ToString(CultureInfo.InvariantCulture),
                descriptor.Category,
                LevelOf(descriptor.DefaultSeverity),
                HelpUriFor(descriptor.Category));
        }

        // A result whose rule is not registered (an analyzer failure reported by Roslyn
        // itself, say) still needs a rule to point at.
        foreach (var violation in reported)
        {
            if (!rules.ContainsKey(violation.RuleId))
            {
                rules[violation.RuleId] = new Rule(violation.RuleId, violation.RuleId, "Other", "warning", null);
            }
        }

        return rules.Values.OrderBy(r => r.Id, StringComparer.Ordinal).ToList();
    }

    private static JsonNode RuleJson(Rule rule)
    {
        var json = new JsonObject
        {
            ["id"] = rule.Id,
            ["shortDescription"] = Text(rule.Title),
            ["fullDescription"] = Text(rule.Title),
            ["help"] = Text(rule.HelpUri is null ? rule.Title : $"{rule.Title}. Documentation: {rule.HelpUri}"),
        };

        if (rule.HelpUri is not null)
        {
            json["helpUri"] = rule.HelpUri;
        }

        json["defaultConfiguration"] = new JsonObject { ["level"] = rule.Level };
        json["properties"] = new JsonObject
        {
            ["category"] = rule.Category,
            ["tags"] = new JsonArray(rule.Category),
        };
        return json;
    }

    // Where each family of rules is explained. The migration rules have their own page; the
    // coverage diagnostics are about configuration; the principle rules are listed, with the
    // principle each enforces, in the README.
    private static string HelpUriFor(string category) => category switch
    {
        "Migration" => InformationUri + "/blob/main/docs/rules/MIGRATION.md",
        "Meta" => InformationUri + "/blob/main/docs/CONFIGURATION.md#when-a-convention-matches-nothing",
        _ => InformationUri + "#what-it-checks-today",
    };

    private static JsonNode ResultFor(
        Violation violation, int ruleIndex, string scanRoot, string sourceRoot, bool baselineInUse)
    {
        var json = new JsonObject
        {
            ["ruleId"] = violation.RuleId,
            ["ruleIndex"] = ruleIndex,
            ["level"] = LevelOf(violation.Severity),
            ["message"] = Text(violation.Message),
        };

        // A repository-wide finding has no file to point at. SARIF allows a result without
        // a location; it cannot sit on a changed line, so a diff-scoped log never has one.
        if (!string.IsNullOrEmpty(violation.FilePath))
        {
            json["locations"] = new JsonArray(new JsonObject
            {
                ["physicalLocation"] = new JsonObject
                {
                    ["artifactLocation"] = new JsonObject
                    {
                        ["uri"] = ArtifactUri(scanRoot, sourceRoot, violation.FilePath),
                        ["uriBaseId"] = SourceRootBaseId,
                    },
                    ["region"] = new JsonObject { ["startLine"] = violation.Line },
                },
            });
        }

        if (violation.Fingerprint is not null)
        {
            json["partialFingerprints"] = new JsonObject { [FindingFingerprint.Version] = violation.Fingerprint };
        }

        // Only the findings the baseline did not accept are written, so against a baseline
        // every one of them is, in SARIF's terms, new.
        if (baselineInUse)
        {
            json["baselineState"] = "new";
        }

        return json;
    }

    private static JsonNode InvocationFor(ScanResult result)
    {
        var notifications = new JsonArray();
        if (result.Gate?.DegradedReason is { } degradedReason)
        {
            notifications.Add(Notification(
                "warning",
                "The changed-lines filter could not be applied, so these results cover the whole scan: " +
                degradedReason));
        }

        if (result.Configuration?.Error is { } configurationError)
        {
            notifications.Add(Notification(
                "warning",
                "The configuration file could not be read, so the scan used the built-in conventions: " +
                configurationError));
        }

        if (result.Baseline is { } baseline && baseline.EntryCount > baseline.AcceptedCount)
        {
            var stale = baseline.EntryCount - baseline.AcceptedCount;
            notifications.Add(Notification(
                "note",
                $"{stale} of the baseline's {baseline.EntryCount} entries matched no finding in this scan. " +
                "Rewrite the baseline with --write-baseline to drop them."));
        }

        var invocation = new JsonObject { ["executionSuccessful"] = true };
        if (notifications.Count > 0)
        {
            invocation["toolExecutionNotifications"] = notifications;
        }

        return invocation;
    }

    private static JsonObject Notification(string level, string message) =>
        new() { ["level"] = level, ["message"] = Text(message) };

    private static JsonObject Text(string text) => new() { ["text"] = text };

    private static string LevelOf(string severity) => severity switch
    {
        "error" => "error",
        "warning" => "warning",
        _ => "note",
    };

    private static string LevelOf(DiagnosticSeverity severity) => severity switch
    {
        DiagnosticSeverity.Error => "error",
        DiagnosticSeverity.Warning => "warning",
        _ => "note",
    };

    // The violation's path, relative to the repository root rather than to the scanned
    // directory, as a URI reference: every segment percent-encoded, so a space or a '#' in a
    // file name cannot produce an invalid URI or be read as a fragment.
    private static string ArtifactUri(string scanRoot, string sourceRoot, string filePath)
    {
        var absolute = Path.GetFullPath(Path.Combine(scanRoot, filePath.Replace('/', Path.DirectorySeparatorChar)));
        var relative = Path.GetRelativePath(Path.GetFullPath(sourceRoot), absolute)
            .Replace(Path.DirectorySeparatorChar, '/');
        return string.Join('/', relative.Split('/').Select(Uri.EscapeDataString));
    }

    // An absolute file URI for a directory, ending in '/' as SARIF requires of a base URI.
    // Built by hand rather than through System.Uri, which would read a '#' in a directory
    // name as the start of a fragment. A Windows drive ("C:") stays unescaped.
    private static string DirectoryUri(string directory)
    {
        var segments = Path.GetFullPath(directory).Replace(Path.DirectorySeparatorChar, '/').Split('/');
        var escaped = segments.Select((segment, i) =>
            i == 0 && segment.Length == 2 && segment[1] == ':' ? segment : Uri.EscapeDataString(segment));
        var path = string.Join('/', escaped);
        return "file://" + (path.StartsWith('/') ? "" : "/") + path + (path.EndsWith('/') ? "" : "/");
    }
}
