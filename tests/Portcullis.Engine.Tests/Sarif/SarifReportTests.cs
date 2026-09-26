using System.Text.Json.Nodes;
using Portcullis.Engine.Findings;
using Portcullis.Engine.Model;
using Portcullis.Engine.Provenance;
using Portcullis.Engine.Sarif;
using Portcullis.Rules;

namespace Portcullis.Engine.Tests.Sarif;

/// <summary>
/// `portcullis scan --sarif`: real scans rendered as SARIF, every one validated against the
/// official schema, and the two filters — changed lines and the baseline — checked for what
/// they keep, what they drop, and that what they drop is counted rather than lost.
/// </summary>
public class SarifReportTests : IDisposable
{
    private static readonly string ConfigurationRule = ConfigurationManagerAnalyzer.ConfigurationManagerRule.Id;
    private static readonly string SyncOverAsyncRule = SyncOverAsyncAnalyzer.SyncOverAsyncRule.Id;

    // PORTCULLIS_MIG_CONFIGURATION_MANAGER, an error, on line 7.
    private static string SettingsClass(string name) => $$"""
        using System.Configuration;

        namespace Shop;

        public static class {{name}}
        {
            public static string Read() => ConfigurationManager.AppSettings["{{name}}"];
        }
        """;

    // PORTCULLIS_MIG_SYNC_OVER_ASYNC, a warning, on line 7.
    private const string Orders = """
        using System.Threading.Tasks;

        namespace Shop;

        public static class Orders
        {
            public static int Count(Task<int> pending) => pending.Result;
        }
        """;

    // A space and a '#' in the repository root, so the base URI has to be escaped too.
    private readonly DirectoryInfo _repo = Directory.CreateTempSubdirectory("portcullis sarif #");

    public void Dispose() => _repo.Delete(recursive: true);

    private string Src => Path.Combine(_repo.FullName, "src");

    private async Task WriteAsync(string relativeToSrc, string content)
    {
        var path = Path.Combine(Src, relativeToSrc);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        await File.WriteAllTextAsync(path, content);
    }

    private static ProvenanceReport Changed(params (string FileFromRepoRoot, int Line)[] lines) =>
        new("1.0.0", "base..head", lines
            .Select(l => new ProvenanceRange(l.FileFromRepoRoot, l.Line, l.Line, ProvenanceSource.Human, 1.0,
                new ProvenanceAttribution(null, "test", "deadbeef")))
            .ToList());

    private static JsonObject Run(string sarif) => (JsonObject)JsonNode.Parse(sarif)!["runs"]![0]!;

    private static JsonArray Results(string sarif) => (JsonArray)Run(sarif)["results"]!;

    private static string AssertValid(string sarif)
    {
        var errors = SarifSchema.Validate(sarif);
        Assert.True(errors.Count == 0, "not valid SARIF 2.1.0:\n" + string.Join("\n", errors));
        return sarif;
    }

    [Fact]
    public async Task PullRequestScan_KeepsOnlyUnacceptedFindingsOnChangedLines_AsValidSarif()
    {
        // Settings: changed line, accepted by the baseline. Pricing: changed line, new.
        // Legacy: new, but not on a changed line. Orders: a warning on a changed line.
        await WriteAsync("Shop/Settings.cs", SettingsClass("Settings"));
        var accepted = (await Scanner.ScanAsync(Src)).Violations.Single(v => v.FilePath == "Shop/Settings.cs");
        await WriteAsync("Shop/Pricing.cs", SettingsClass("Pricing"));
        await WriteAsync("Shop/Legacy.cs", SettingsClass("Legacy"));
        await WriteAsync("Shop/Orders.cs", Orders);
        var provenance = Changed(("src/Shop/Settings.cs", 7), ("src/Shop/Pricing.cs", 7), ("src/Shop/Orders.cs", 7));

        var result = await Scanner.ScanAsync(Src, provenance, _repo.FullName, Baseline.Of([accepted.Fingerprint!], "b.json"));
        var changedLines = ChangedLines.From(provenance, _repo.FullName);
        var sarif = AssertValid(SarifReport.Serialize(result, _repo.FullName, changedLines));

        var results = Results(sarif);
        Assert.Equal(
            new[] { ("src/Shop/Orders.cs", SyncOverAsyncRule, "warning"), ("src/Shop/Pricing.cs", ConfigurationRule, "error") },
            results.Select(r => (
                (string)r!["locations"]![0]!["physicalLocation"]!["artifactLocation"]!["uri"]!,
                (string)r["ruleId"]!,
                (string)r["level"]!)));

        var properties = Run(sarif)["properties"]!;
        Assert.Equal("diff", (string?)properties["scope"]);
        Assert.Equal(result.Violations.Count, (int)properties["findingsInScan"]!);
        Assert.Equal(result.Violations.Count(v => !changedLines.Contains(v, result.ScannedPath)), (int)properties["excludedOutsideChangedLines"]!);
        Assert.Equal(1, (int)properties["excludedByBaseline"]!);
    }

    [Fact]
    public async Task EachResult_CarriesRuleIdAndIndex_Level_Message_LocationWithRegion_AndItsFingerprint()
    {
        await WriteAsync("Shop/Pricing.cs", SettingsClass("Pricing"));
        var result = await Scanner.ScanAsync(Src, provenanceRoot: _repo.FullName);
        var violation = result.Violations.Single(v => v.RuleId == ConfigurationRule);

        var sarif = AssertValid(SarifReport.Serialize(result, _repo.FullName));

        var sarifResult = Results(sarif).Single(r => (string)r!["ruleId"]! == ConfigurationRule)!;
        var rules = (JsonArray)Run(sarif)["tool"]!["driver"]!["rules"]!;
        Assert.Equal(ConfigurationRule, (string)rules[(int)sarifResult["ruleIndex"]!]!["id"]!);
        Assert.Equal("error", (string)sarifResult["level"]!);
        Assert.Equal(violation.Message, (string)sarifResult["message"]!["text"]!);

        var location = sarifResult["locations"]![0]!["physicalLocation"]!;
        Assert.Equal("src/Shop/Pricing.cs", (string)location["artifactLocation"]!["uri"]!);
        Assert.Equal(SarifReport.SourceRootBaseId, (string)location["artifactLocation"]!["uriBaseId"]!);
        Assert.Equal(7, (int)location["region"]!["startLine"]!);

        Assert.Equal(violation.Fingerprint, (string)sarifResult["partialFingerprints"]![FindingFingerprint.Version]!);
        Assert.Null(sarifResult["baselineState"]);
    }

    [Fact]
    public async Task TheLog_NamesTheSchemaVersionToolAndRepositoryRoot()
    {
        await WriteAsync("Shop/Pricing.cs", SettingsClass("Pricing"));
        var result = await Scanner.ScanAsync(Src);

        var log = JsonNode.Parse(AssertValid(SarifReport.Serialize(result, _repo.FullName)))!;

        Assert.Equal(SarifReport.SchemaUri, (string)log["$schema"]!);
        Assert.Equal("2.1.0", (string)log["version"]!);
        var driver = log["runs"]![0]!["tool"]!["driver"]!;
        Assert.Equal("Portcullis", (string)driver["name"]!);
        Assert.Equal(result.EngineVersion, (string)driver["version"]!);

        var root = (string)log["runs"]![0]!["originalUriBaseIds"]![SarifReport.SourceRootBaseId]!["uri"]!;
        Assert.StartsWith("file:///", root);
        Assert.EndsWith("/", root);
        Assert.Contains("portcullis%20sarif%20%23", root);
        Assert.Equal(_repo.FullName, new Uri(root).LocalPath.TrimEnd('/', '\\'));
    }

    [Fact]
    public async Task EveryRegisteredRuleIsDescribed_SortedById_WithHelpForItsFamily()
    {
        var result = await Scanner.ScanAsync(Src);

        var rules = ((JsonArray)Run(AssertValid(SarifReport.Serialize(result, _repo.FullName)))["tool"]!["driver"]!["rules"]!)
            .Select(r => r!)
            .ToList();

        var registered = RuleRegistry.All.SelectMany(a => a.SupportedDiagnostics).ToList();
        Assert.Equal(
            registered.Select(d => d.Id).Order(StringComparer.Ordinal),
            rules.Select(r => (string)r["id"]!));
        Assert.All(rules, r =>
        {
            Assert.False(string.IsNullOrWhiteSpace((string?)r["shortDescription"]!["text"]));
            Assert.False(string.IsNullOrWhiteSpace((string?)r["fullDescription"]!["text"]));
            Assert.False(string.IsNullOrWhiteSpace((string?)r["help"]!["text"]));
        });

        var migration = rules.Single(r => (string)r["id"]! == ConfigurationRule);
        Assert.EndsWith("/docs/rules/MIGRATION.md", (string)migration["helpUri"]!);
        Assert.Equal("error", (string)migration["defaultConfiguration"]!["level"]!);
        Assert.Equal("Migration", (string)migration["properties"]!["category"]!);
        var syncOverAsync = rules.Single(r => (string)r["id"]! == SyncOverAsyncRule);
        Assert.Equal("warning", (string)syncOverAsync["defaultConfiguration"]!["level"]!);
    }

    [Fact]
    public async Task WithoutADiffOrABaseline_EveryFindingIsAResult_IncludingRepositoryWideOnesWithoutALocation()
    {
        await WriteAsync("Shop/Pricing.cs", SettingsClass("Pricing"));
        var result = await Scanner.ScanAsync(Src, provenanceRoot: _repo.FullName);
        Assert.Contains(result.Violations, v => v.FilePath.Length == 0);

        var sarif = AssertValid(SarifReport.Serialize(result, _repo.FullName));

        var results = Results(sarif);
        Assert.Equal(result.Violations.Select(v => v.RuleId), results.Select(r => (string)r!["ruleId"]!));
        var repositoryWide = results.Single(r => r!["locations"] is null)!;
        Assert.Equal(ConventionCoverageAnalyzer.NoConventionMatchedRule.Id, (string)repositoryWide["ruleId"]!);
        Assert.Equal("all", (string?)Run(sarif)["properties"]!["scope"]);
    }

    [Fact]
    public async Task AgainstABaseline_ResultsAreMarkedNew_AndStaleEntriesAreNoted()
    {
        await WriteAsync("Shop/Pricing.cs", SettingsClass("Pricing"));
        var result = await Scanner.ScanAsync(Src, baseline: Baseline.Of(["no-longer-found"]));

        var sarif = AssertValid(SarifReport.Serialize(result, _repo.FullName));

        Assert.All(Results(sarif), r => Assert.Equal("new", (string)r!["baselineState"]!));
        var notification = Assert.Single((JsonArray)Run(sarif)["invocations"]![0]!["toolExecutionNotifications"]!)!;
        Assert.Equal("note", (string)notification["level"]!);
        Assert.Contains("1 of the baseline's 1 entries matched no finding", (string)notification["message"]!["text"]!);
    }

    [Fact]
    public async Task ADegradedDiffScope_CoversTheWholeScan_AndSaysWhy()
    {
        await WriteAsync("Shop/Pricing.cs", SettingsClass("Pricing"));
        var degraded = new ProvenanceReport("1.0.0", "base..head", [], ProvenanceStatus.Degraded, "git could not resolve 'base'");
        var result = await Scanner.ScanAsync(Src, degraded, _repo.FullName);

        // The CLI passes no changed lines when the gate fell back to the whole scan.
        var sarif = AssertValid(SarifReport.Serialize(result, _repo.FullName));

        Assert.Equal(result.Violations.Count, Results(sarif).Count);
        var invocation = Run(sarif)["invocations"]![0]!;
        Assert.True((bool)invocation["executionSuccessful"]!);
        var notification = Assert.Single((JsonArray)invocation["toolExecutionNotifications"]!)!;
        Assert.Equal("warning", (string)notification["level"]!);
        Assert.Contains("git could not resolve 'base'", (string)notification["message"]!["text"]!);
    }

    [Fact]
    public async Task AnUnreadableConfigFile_IsANotification()
    {
        await WriteAsync("Shop/Pricing.cs", SettingsClass("Pricing"));
        await File.WriteAllTextAsync(Path.Combine(Src, "portcullis.json"), "{ not json");
        var result = await Scanner.ScanAsync(Src);

        var sarif = AssertValid(SarifReport.Serialize(result, _repo.FullName));

        var notification = Assert.Single((JsonArray)Run(sarif)["invocations"]![0]!["toolExecutionNotifications"]!)!;
        Assert.Contains("built-in conventions", (string)notification["message"]!["text"]!);
    }

    [Fact]
    public async Task FileNamesThatAreNotValidInAUri_AreEscaped()
    {
        await WriteAsync("My Folder #1/Café Settings.cs", SettingsClass("Pricing"));
        var result = await Scanner.ScanAsync(Src);

        var sarif = AssertValid(SarifReport.Serialize(result, _repo.FullName));

        var uri = (string)Results(sarif).Single(r => (string)r!["ruleId"]! == ConfigurationRule)!
            ["locations"]![0]!["physicalLocation"]!["artifactLocation"]!["uri"]!;
        Assert.Equal("src/My%20Folder%20%231/Caf%C3%A9%20Settings.cs", uri);
    }

    [Fact]
    public void Levels_MapErrorWarningAndInfoToSarifLevels_AndAnUnregisteredRuleStillGetsADescriptor()
    {
        var result = HandBuilt(
            new Violation("CUSTOM_ERROR", "A.cs", 1, "e", "error", "f1"),
            new Violation("CUSTOM_WARNING", "A.cs", 2, "w", "warning", "f2"),
            new Violation("CUSTOM_INFO", "A.cs", 3, "i", "info", "f3"));

        var sarif = AssertValid(SarifReport.Serialize(result, result.ScannedPath));

        Assert.Equal(new[] { "error", "warning", "note" }, Results(sarif).Select(r => (string)r!["level"]!));
        var rules = (JsonArray)Run(sarif)["tool"]!["driver"]!["rules"]!;
        foreach (var r in Results(sarif))
        {
            Assert.Equal((string)r!["ruleId"]!, (string)rules[(int)r["ruleIndex"]!]!["id"]!);
        }
    }

    [Fact]
    public async Task Serialize_IsDeterministic()
    {
        await WriteAsync("Shop/Pricing.cs", SettingsClass("Pricing"));
        await WriteAsync("Shop/Orders.cs", Orders);
        var result = await Scanner.ScanAsync(Src);

        var first = SarifReport.Serialize(result, _repo.FullName);
        var second = SarifReport.Serialize(result, _repo.FullName);

        Assert.Equal(first, second);
        Assert.DoesNotContain("\r", first);
        Assert.EndsWith("}\n", first);
    }

    private ScanResult HandBuilt(params Violation[] violations) => new(
        Scanner.SchemaVersion, "0.0.0-test", _repo.FullName, DateTime.UnixEpoch, 0, 1, 0,
        violations, ScanSummary.From(violations), new GateResult(false, "all", 0));
}
