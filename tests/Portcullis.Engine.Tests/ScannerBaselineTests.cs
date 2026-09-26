using Portcullis.Engine.Findings;
using Portcullis.Engine.Model;
using Portcullis.Engine.Provenance;
using Portcullis.Rules;

namespace Portcullis.Engine.Tests;

/// <summary>
/// Fingerprints and the baseline through the scanner itself, against the registered rules:
/// every finding gets an identity that survives edits around it, and a finding the baseline
/// accepts is still reported but no longer blocks — in the whole-scan gate, the diff-scoped
/// gate and the degraded fallback alike (docs/SARIF.md).
/// </summary>
public class ScannerBaselineTests : IDisposable
{
    private static readonly string ConfigurationRule = ConfigurationManagerAnalyzer.ConfigurationManagerRule.Id;

    // PORTCULLIS_MIG_CONFIGURATION_MANAGER, an error, on line 7.
    private const string Settings = """
        using System.Configuration;

        namespace Shop;

        public static class Settings
        {
            public static string Currency() => ConfigurationManager.AppSettings["Currency"];
        }
        """;

    // The same error on line 7 of a second file.
    private const string Pricing = """
        using System.Configuration;

        namespace Shop;

        public static class Pricing
        {
            public static string Region() => ConfigurationManager.AppSettings["Region"];
        }
        """;

    private readonly DirectoryInfo _dir = Directory.CreateTempSubdirectory("portcullis-baseline-scan-");

    public void Dispose() => _dir.Delete(recursive: true);

    private Task WriteAsync(string name, string content) =>
        File.WriteAllTextAsync(Path.Combine(_dir.FullName, name), content);

    private static Violation Single(ScanResult result, string file) =>
        Assert.Single(result.Violations, v => v.RuleId == ConfigurationRule && v.FilePath == file);

    private static ProvenanceReport Changed(params (string File, int Line)[] lines) =>
        new("1.0.0", "base..head", lines
            .Select(l => new ProvenanceRange(l.File, l.Line, l.Line, ProvenanceSource.Human, 1.0,
                new ProvenanceAttribution(null, "test", "deadbeef")))
            .ToList());

    [Fact]
    public async Task ScanAsync_GivesEveryViolationAFingerprint_AndIdenticalLinesDistinctOnes()
    {
        await WriteAsync("Twice.cs", """
            using System.Configuration;

            namespace Shop;

            public static class Twice
            {
                public static string A()
                {
                    return ConfigurationManager.AppSettings["Currency"];
                }

                public static string B()
                {
                    return ConfigurationManager.AppSettings["Currency"];
                }
            }
            """);

        var result = await Scanner.ScanAsync(_dir.FullName);

        Assert.NotEmpty(result.Violations);
        Assert.All(result.Violations, v => Assert.Matches("^[0-9a-f]{64}$", v.Fingerprint!));
        Assert.Equal(result.Violations.Count, result.Violations.Select(v => v.Fingerprint).Distinct().Count());

        var twice = result.Violations.Where(v => v.RuleId == ConfigurationRule).ToList();
        Assert.Equal(new[] { 9, 14 }, twice.Select(v => v.Line));
        Assert.Equal(
            FindingFingerprint.Compute(ConfigurationRule, "Twice.cs", "return ConfigurationManager.AppSettings[\"Currency\"];", 0),
            twice[0].Fingerprint);
        Assert.Equal(
            FindingFingerprint.Compute(ConfigurationRule, "Twice.cs", "return ConfigurationManager.AppSettings[\"Currency\"];", 1),
            twice[1].Fingerprint);
    }

    [Fact]
    public async Task ScanAsync_Fingerprint_SurvivesLinesAddedAboveAndReindentation_ButNotARewriteOfItsLine()
    {
        await WriteAsync("Settings.cs", Settings);
        var original = Single(await Scanner.ScanAsync(_dir.FullName), "Settings.cs");

        await WriteAsync("Settings.cs", "// A header someone added.\n// And another line.\n" +
            Settings.Replace("    public static string Currency()", "        public static string Currency()"));
        var moved = Single(await Scanner.ScanAsync(_dir.FullName), "Settings.cs");

        await WriteAsync("Settings.cs", Settings.Replace("\"Currency\"", "\"Locale\""));
        var rewritten = Single(await Scanner.ScanAsync(_dir.FullName), "Settings.cs");

        Assert.Equal(7, original.Line);
        Assert.Equal(9, moved.Line);
        Assert.Equal(original.Fingerprint, moved.Fingerprint);
        Assert.NotEqual(original.Fingerprint, rewritten.Fingerprint);
    }

    [Fact]
    public async Task ScanAsync_NoBaseline_MarksNothingBaselinedAndReportsNoBaseline()
    {
        await WriteAsync("Settings.cs", Settings);

        var result = await Scanner.ScanAsync(_dir.FullName);

        Assert.All(result.Violations, v => Assert.False(v.Baselined));
        Assert.Null(result.Baseline);
        Assert.Equal(0, result.Gate!.AcceptedByBaselineCount);
    }

    [Fact]
    public async Task ScanAsync_WithBaseline_AnAcceptedErrorIsStillReportedButDoesNotBlock()
    {
        await WriteAsync("Settings.cs", Settings);
        var accepted = Single(await Scanner.ScanAsync(_dir.FullName), "Settings.cs");

        var result = await Scanner.ScanAsync(_dir.FullName, baseline: Baseline.Of([accepted.Fingerprint!], "baseline.json"));

        var violation = Single(result, "Settings.cs");
        Assert.True(violation.Baselined);
        Assert.Equal("error", violation.Severity);
        Assert.Equal(1, result.Summary.ErrorCount);
        Assert.Equal(new GateResult(false, "all", 0, AcceptedByBaselineCount: 1), result.Gate);
        Assert.Equal(new ScanBaseline("baseline.json", 1, 1), result.Baseline);
    }

    [Fact]
    public async Task ScanAsync_WithBaseline_ANewErrorStillBlocks()
    {
        await WriteAsync("Settings.cs", Settings);
        var accepted = Single(await Scanner.ScanAsync(_dir.FullName), "Settings.cs");
        await WriteAsync("Pricing.cs", Pricing);

        var result = await Scanner.ScanAsync(_dir.FullName, baseline: Baseline.Of([accepted.Fingerprint!]));

        Assert.False(Single(result, "Pricing.cs").Baselined);
        Assert.Equal(new GateResult(true, "all", 1, AcceptedByBaselineCount: 1), result.Gate);
    }

    [Fact]
    public async Task ScanAsync_DiffScopeWithBaseline_BlocksOnlyOnUnacceptedErrorsOnChangedLines()
    {
        // Settings.cs:7 is on a changed line but accepted; Pricing.cs:7 is on a changed line
        // and new; Legacy.cs:7 is new but outside the diff.
        await WriteAsync("Settings.cs", Settings);
        var accepted = Single(await Scanner.ScanAsync(_dir.FullName), "Settings.cs");
        await WriteAsync("Pricing.cs", Pricing);
        await WriteAsync("Legacy.cs", Pricing.Replace("class Pricing", "class Legacy"));

        var result = await Scanner.ScanAsync(
            _dir.FullName,
            Changed(("Settings.cs", 7), ("Pricing.cs", 7)),
            _dir.FullName,
            Baseline.Of([accepted.Fingerprint!]));

        Assert.Equal(new GateResult(true, "diff", 1, AcceptedByBaselineCount: 1), result.Gate);
        Assert.Equal(3, result.Summary.ErrorCount);
    }

    [Fact]
    public async Task ScanAsync_DegradedProvenanceWithBaseline_FallsBackToTheWholeScanAndStillHonoursTheBaseline()
    {
        await WriteAsync("Settings.cs", Settings);
        var accepted = Single(await Scanner.ScanAsync(_dir.FullName), "Settings.cs");
        var degraded = new ProvenanceReport("1.0.0", "base..head", [], ProvenanceStatus.Degraded, "git is unavailable");

        var result = await Scanner.ScanAsync(_dir.FullName, degraded, baseline: Baseline.Of([accepted.Fingerprint!]));

        Assert.Equal(new GateResult(false, "all", 0, "git is unavailable", AcceptedByBaselineCount: 1), result.Gate);
    }

    [Fact]
    public async Task ScanAsync_BaselineAcceptingEverything_PassesAndCountsTheWholeScanAsItsEntries()
    {
        // What --write-baseline gates against: the findings about to be written.
        await WriteAsync("Settings.cs", Settings);
        await WriteAsync("Pricing.cs", Pricing);

        var result = await Scanner.ScanAsync(_dir.FullName, baseline: Baseline.Everything("new-baseline.json"));

        Assert.All(result.Violations, v => Assert.True(v.Baselined));
        Assert.False(result.Gate!.Blocked);
        Assert.Equal(2, result.Gate.AcceptedByBaselineCount);
        Assert.Equal(
            new ScanBaseline("new-baseline.json", result.Violations.Count, result.Violations.Count),
            result.Baseline);
    }

    [Fact]
    public async Task ScanAsync_BaselineEntriesThatMatchNothing_AreCountedAsEntriesButNotAsAccepted()
    {
        await WriteAsync("Settings.cs", Settings);

        var result = await Scanner.ScanAsync(_dir.FullName, baseline: Baseline.Of(["gone-1", "gone-2"]));

        Assert.Equal(new ScanBaseline(null, 2, 0), result.Baseline);
        Assert.True(result.Gate!.Blocked);
    }

    [Fact]
    public async Task ScanAsync_TwoFindingsOnOneLine_ComeOutInColumnOrderEveryTime()
    {
        // By message, HttpRequest would sort first; by column HttpResponse does. Analyzers run
        // concurrently, so without a column tiebreak the order depended on which finished first.
        await WriteAsync("Handler.cs", """
            namespace Shop;

            public static class Handler
            {
                public static void Handle(System.Web.HttpResponse response, System.Web.HttpRequest request) { }
            }
            """);

        var first = await Scanner.ScanAsync(_dir.FullName);
        var second = await Scanner.ScanAsync(_dir.FullName);

        var onLine = first.Violations.Where(v => v.RuleId == SystemWebUsageAnalyzer.SystemWebRule.Id).ToList();
        Assert.Equal(2, onLine.Count);
        Assert.Contains("HttpResponse", onLine[0].Message);
        Assert.Contains("HttpRequest", onLine[1].Message);
        Assert.Equal(first.Violations, second.Violations);
    }
}
