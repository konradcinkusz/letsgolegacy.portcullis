using Portcullis.Rules;

namespace Portcullis.Engine.Tests.Rules;

/// <summary>
/// The coverage diagnostics. Half of these tests assert the rule stays SILENT, which is
/// the load-bearing half: a meta-rule that fires on healthy code gets switched off, and a
/// rule that is switched off reports nothing when it matters either.
/// </summary>
public class ConventionCoverageAnalyzerTests
{
    private const string TrivialSource = "namespace Demo;\n\npublic class Thing { }\n";

    [Fact]
    public async Task NoConventionMatched_FiresOnceWhenNothingIsConfiguredAndNothingMatches()
    {
        var diagnostics = await AnalyzerTestHelper.GetDiagnosticsAsync(
            new ConventionCoverageAnalyzer(),
            ("src/Whatever/Thing.cs", TrivialSource));

        var hit = Assert.Single(diagnostics, d => d.Id == ConventionCoverageAnalyzer.NoConventionMatchedRule.Id);
        Assert.Contains("portcullis.json", hit.GetMessage());
    }

    [Fact]
    public async Task NoConventionMatched_StaysSilentWhenAnyConventionMatches()
    {
        // One matching convention is enough: the rules are engaged, and this codebase
        // simply may not have every shape. Reporting per-convention here is what made an
        // earlier cut of this rule produce four warnings on ordinary code.
        var diagnostics = await AnalyzerTestHelper.GetDiagnosticsAsync(
            new ConventionCoverageAnalyzer(),
            ("src/Demo.ServiceDefaults/Extensions.cs", TrivialSource),
            ("src/Demo.Api/Thing.cs", TrivialSource));

        Assert.DoesNotContain(diagnostics, d => d.Id == ConventionCoverageAnalyzer.NoConventionMatchedRule.Id);
    }

    [Fact]
    public async Task NoConventionMatched_StaysSilentWhenAConfiguredConventionMatchesNothing()
    {
        // Pins the ExplicitlyConfiguredKeys half of the guard specifically: NOTHING here
        // matches, so the !anyMatched clause is satisfied and only the "was anything
        // configured" clause can keep this quiet. A team who declared a name gets the
        // precise PORTCULLIS_CONVENTION_UNMATCHED instead of the vague aggregate one.
        var config = new Dictionary<string, string> { [PortcullisConventionKeys.EntityFolders] = "Model" };

        var diagnostics = await AnalyzerTestHelper.GetDiagnosticsAsync(
            new ConventionCoverageAnalyzer(), config,
            ("src/Demo.Api/Whatever/Thing.cs", TrivialSource));

        Assert.DoesNotContain(diagnostics, d => d.Id == ConventionCoverageAnalyzer.NoConventionMatchedRule.Id);
        Assert.Single(diagnostics, d => d.Id == ConventionCoverageAnalyzer.ConventionUnmatchedRule.Id);
    }

    [Fact]
    public async Task NoConventionMatched_StillFiresWhenOnlyANonCoverageKeyWasConfigured()
    {
        // The guard must count only the conventions these checks actually consult.
        // Guarding on "configured anything at all" let an unrelated setting — a line
        // ceiling, which names no folder — silence the vacuous-scan report entirely.
        var config = new Dictionary<string, string> { [PortcullisConventionKeys.KernelLineCeiling] = "500" };

        var diagnostics = await AnalyzerTestHelper.GetDiagnosticsAsync(
            new ConventionCoverageAnalyzer(), config,
            ("src/Demo.Api/Whatever/Thing.cs", TrivialSource));

        Assert.Single(diagnostics, d => d.Id == ConventionCoverageAnalyzer.NoConventionMatchedRule.Id);
    }

    [Fact]
    public async Task NoConventionMatched_IgnoresFoldersAboveTheScanRoot()
    {
        // Conventions match relative to the configured path root. Without that, a
        // checkout living under a directory called "Domain" made every file an entity
        // file, silently satisfying the convention and suppressing this report.
        var config = new Dictionary<string, string>
        {
            [PortcullisConventionKeys.PathRoot] = "/home/dev/Domain/myproj",
        };

        var diagnostics = await AnalyzerTestHelper.GetDiagnosticsAsync(
            new ConventionCoverageAnalyzer(), config,
            ("/home/dev/Domain/myproj/App/Thing.cs", TrivialSource));

        Assert.Single(diagnostics, d => d.Id == ConventionCoverageAnalyzer.NoConventionMatchedRule.Id);
    }

    [Fact]
    public async Task NoConventionMatched_StaysSilentOnAnEmptyCompilation()
    {
        var diagnostics = await AnalyzerTestHelper.GetDiagnosticsAsync(new ConventionCoverageAnalyzer());

        Assert.Empty(diagnostics);
    }

    [Fact]
    public async Task ConventionUnmatched_FiresForAConfiguredNameThatMatchesNothing()
    {
        // The high-confidence case: the team wrote "Kernell" and no such folder exists.
        var config = new Dictionary<string, string> { [PortcullisConventionKeys.KernelFolders] = "Kernell" };

        var diagnostics = await AnalyzerTestHelper.GetDiagnosticsAsync(
            new ConventionCoverageAnalyzer(), config,
            ("src/Demo/Kernel/Thing.cs", TrivialSource));

        var hit = Assert.Single(diagnostics, d => d.Id == ConventionCoverageAnalyzer.ConventionUnmatchedRule.Id);
        Assert.Contains(PortcullisConventionKeys.KernelFolders, hit.GetMessage());
        Assert.Contains("Kernell", hit.GetMessage());
    }

    [Fact]
    public async Task ConventionUnmatched_StaysSilentForAConfiguredNameThatMatches()
    {
        var config = new Dictionary<string, string> { [PortcullisConventionKeys.KernelFolders] = "Common" };

        var diagnostics = await AnalyzerTestHelper.GetDiagnosticsAsync(
            new ConventionCoverageAnalyzer(), config,
            ("src/Demo/Common/Thing.cs", TrivialSource));

        Assert.DoesNotContain(diagnostics, d => d.Id == ConventionCoverageAnalyzer.ConventionUnmatchedRule.Id);
    }

    [Fact]
    public async Task ConventionUnmatched_StaysSilentForAnUnconfiguredDefaultThatMatchesNothing()
    {
        // A built-in default matching nothing is not evidence of a mistake — most
        // codebases lack some of these shapes. Only the aggregate rule speaks here.
        var diagnostics = await AnalyzerTestHelper.GetDiagnosticsAsync(
            new ConventionCoverageAnalyzer(),
            ("src/Demo.ServiceDefaults/Extensions.cs", TrivialSource));

        Assert.DoesNotContain(diagnostics, d => d.Id == ConventionCoverageAnalyzer.ConventionUnmatchedRule.Id);
    }

    [Fact]
    public async Task ConventionUnmatched_StaysSilentForAnExplicitlyEmptyConvention()
    {
        // "[]" is a declaration that the project has none of this shape, not a mistake.
        var config = new Dictionary<string, string> { [PortcullisConventionKeys.KernelFolders] = "" };

        var diagnostics = await AnalyzerTestHelper.GetDiagnosticsAsync(
            new ConventionCoverageAnalyzer(), config,
            ("src/Demo/Whatever/Thing.cs", TrivialSource));

        Assert.DoesNotContain(diagnostics, d => d.Id == ConventionCoverageAnalyzer.ConventionUnmatchedRule.Id);
    }

    [Fact]
    public async Task ConventionUnmatched_ReportsEachConfiguredMissSeparately()
    {
        var config = new Dictionary<string, string>
        {
            [PortcullisConventionKeys.KernelFolders] = "NoSuchKernel",
            [PortcullisConventionKeys.AdapterFolders] = "NoSuchAdapter",
        };

        var diagnostics = await AnalyzerTestHelper.GetDiagnosticsAsync(
            new ConventionCoverageAnalyzer(), config,
            ("src/Demo/Whatever/Thing.cs", TrivialSource));

        Assert.Equal(2, diagnostics.Count(d => d.Id == ConventionCoverageAnalyzer.ConventionUnmatchedRule.Id));
    }

    [Fact]
    public async Task CoverageDiagnostics_AreReportedWithoutAFileLocation()
    {
        // A statement about the whole compilation, not about one file. Pinned because
        // Scanner and CommentFormatter both have a branch for exactly this shape.
        var diagnostics = await AnalyzerTestHelper.GetDiagnosticsAsync(
            new ConventionCoverageAnalyzer(),
            ("src/Whatever/Thing.cs", TrivialSource));

        var hit = Assert.Single(diagnostics);
        Assert.Equal(Microsoft.CodeAnalysis.Location.None, hit.Location);
    }
}
