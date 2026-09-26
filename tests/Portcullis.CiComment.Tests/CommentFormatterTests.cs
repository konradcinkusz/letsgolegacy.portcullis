using System.Text.Json;
using Portcullis.Engine.Model;

namespace Portcullis.CiComment.Tests;

public class CommentFormatterTests
{
    [Fact]
    public async Task Render_AlwaysStartsWithTheStickyMarker()
    {
        var clean = await LoadFixtureAsync("clean.json");

        var markdown = CommentFormatter.Render(clean, previous: null);

        Assert.StartsWith(StickyComment.Marker, markdown);
    }

    [Fact]
    public async Task Render_CleanResult_ReportsNoViolationsAndNoFileSections()
    {
        var clean = await LoadFixtureAsync("clean.json");

        var markdown = CommentFormatter.Render(clean, previous: null);

        Assert.Contains("No architecture violations found", markdown);
        Assert.DoesNotContain("###", markdown);
    }

    [Fact]
    public void Render_ZeroRulesEvaluated_IsHonestAboutNotHavingCheckedAnythingYet()
    {
        var result = new ScanResult(
            "1.0.0",
            "1.0.0+test",
            "/tmp/scan",
            DateTime.UtcNow,
            12.3,
            6,
            0,
            Array.Empty<Violation>(),
            new ScanSummary(0, 0, 0));

        var markdown = CommentFormatter.Render(result, previous: null);

        Assert.Contains("0 rules evaluated", markdown);
        Assert.DoesNotContain("No architecture violations found", markdown);
    }

    [Fact]
    public async Task Render_MultiViolation_GroupsByFileAndShowsAllThreeSeverities()
    {
        var result = await LoadFixtureAsync("multi-violation.json");

        var markdown = CommentFormatter.Render(result, previous: null);

        Assert.Contains("### `src/ServiceDefaults/Extensions.cs`", markdown);
        Assert.Contains("### `src/Demo.AdvertsService/Controllers/AdvertsController.cs`", markdown);
        Assert.Contains("### `src/Demo.IdentityService/Services/EmailSender.cs`", markdown);
        Assert.Contains("### `src/Demo.IdentityService/Program.cs`", markdown);
        Assert.Contains("PORTCULLIS_P2_KERNEL_LOC_CEILING", markdown);
        Assert.Contains("❌", markdown);
        Assert.Contains("⚠", markdown);
        Assert.Contains("ℹ", markdown);
        Assert.Contains("2 errors", markdown);
        Assert.Contains("2 warnings", markdown);
    }

    [Fact]
    public async Task Render_NeverMentionsProvenanceAiAuthorshipOrEuAiAct()
    {
        var result = await LoadFixtureAsync("multi-violation.json");

        var markdown = CommentFormatter.Render(result, previous: null);

        Assert.DoesNotContain("AI-authored", markdown, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("provenance", markdown, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("EU AI Act", markdown, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Render_BeforeAfterPair_ReportsExactlyOneNewAndOneResolved()
    {
        var before = await LoadFixtureAsync("pr-before.json");
        var after = await LoadFixtureAsync("pr-after.json");

        var markdown = CommentFormatter.Render(after, before);

        Assert.Contains("1 new", markdown);
        Assert.Contains("1 resolved", markdown);
        Assert.Contains("**New:**", markdown);
        Assert.Contains("**Resolved:**", markdown);
        Assert.Contains("PORTCULLIS_P11_VENDOR_SDK_OUTSIDE_ADAPTER", markdown);
    }

    [Fact]
    public async Task Render_SamePreviousAndCurrent_OmitsTheDiffCallout()
    {
        var result = await LoadFixtureAsync("multi-violation.json");

        var markdown = CommentFormatter.Render(result, result);

        Assert.DoesNotContain(" new · ", markdown);
        Assert.DoesNotContain("**New:**", markdown);
        Assert.DoesNotContain("**Resolved:**", markdown);
    }

    [Fact]
    public void Render_DiffScopedGateBlocked_ShowsBlockingNote()
    {
        var violation = new Violation("PORTCULLIS_TEST", "Foo.cs", 5, "test violation", "error");
        var result = new ScanResult(
            "1.0.0", "1.0.0+test", "/tmp/scan", DateTime.UtcNow, 12.3, 1, 1,
            [violation], new ScanSummary(1, 0, 0), new GateResult(true, "diff", 1));

        var markdown = CommentFormatter.Render(result, previous: null);

        Assert.Contains("🚫 **Blocking this PR**", markdown);
        Assert.Contains("1 error within this PR's own changed lines", markdown);
    }

    [Fact]
    public void Render_DiffScopedGateNotBlockedButErrorsExistElsewhere_ShowsNotBlockingNote()
    {
        var violation = new Violation("PORTCULLIS_TEST", "Foo.cs", 5, "test violation", "error");
        var result = new ScanResult(
            "1.0.0", "1.0.0+test", "/tmp/scan", DateTime.UtcNow, 12.3, 1, 1,
            [violation], new ScanSummary(1, 0, 0), new GateResult(false, "diff", 0));

        var markdown = CommentFormatter.Render(result, previous: null);

        Assert.Contains("ℹ️ Not blocking this PR", markdown);
        Assert.Contains("outside this PR's own changed lines", markdown);
    }

    [Fact]
    public async Task Render_AbsoluteScopeOrNoGate_OmitsTheGateNote()
    {
        var result = await LoadFixtureAsync("multi-violation.json"); // predates Gate; deserializes null

        var markdown = CommentFormatter.Render(result, previous: null);

        Assert.DoesNotContain("Blocking this PR", markdown);
        Assert.DoesNotContain("Not blocking this PR", markdown);
    }

    private static async Task<ScanResult> LoadFixtureAsync(string fileName)
    {
        var path = Path.Combine(AppContext.BaseDirectory, "fixtures", fileName);
        var json = await File.ReadAllTextAsync(path);
        return JsonSerializer.Deserialize<ScanResult>(
            json,
            new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase })!;
    }

    [Fact]
    public void Render_DegradedGate_SaysTheScopeIsNotTheOneThatWasRequested()
    {
        // Without this the comment shows pre-existing violations blocking a PR that never
        // touched them, and a reviewer concludes the tool is broken rather than the
        // checkout.
        var result = new ScanResult(
            "1.0.0", "1.0.0+test", "/tmp/scan", DateTime.UtcNow, 12.3, 6, 7,
            [new Violation("PORTCULLIS_P9_CONTROLLER_NO_DBCONTEXT", "src/Foo.cs", 12, "boom", "error")],
            new ScanSummary(1, 0, 0),
            new GateResult(true, "all", 1, "git could not resolve 'abc..def'"));

        var markdown = CommentFormatter.Render(result, previous: null);

        Assert.Contains("Could not scope this gate", markdown);
        Assert.Contains("git could not resolve 'abc..def'", markdown);
    }

    [Fact]
    public void Render_LocationLessViolation_GetsARepositoryWideHeadingRatherThanAnEmptyOne()
    {
        // ConventionCoverageAnalyzer reports against the whole compilation, so its
        // violations arrive with an empty FilePath and line 1. Rendered as a file section
        // that would be an empty `` heading and a meaningless "line 1".
        var result = new ScanResult(
            "1.0.0", "1.0.0+test", "/tmp/scan", DateTime.UtcNow, 12.3, 6, 7,
            [new Violation("PORTCULLIS_NO_CONVENTION_MATCHED", "", 1, "nothing matched", "warning")],
            new ScanSummary(0, 1, 0),
            new GateResult(false, "all", 0));

        var markdown = CommentFormatter.Render(result, previous: null);

        Assert.Contains("### Repository-wide", markdown);
        Assert.DoesNotContain("### ``", markdown);
        Assert.DoesNotContain("line 1", markdown);
    }

    [Fact]
    public void Render_OrdinaryFileScopedViolation_StillShowsItsLineNumber()
    {
        // The control for the test above: the repository-wide branch must not swallow the
        // line number on ordinary violations.
        var result = new ScanResult(
            "1.0.0", "1.0.0+test", "/tmp/scan", DateTime.UtcNow, 12.3, 6, 7,
            [new Violation("PORTCULLIS_P9_CONTROLLER_NO_DBCONTEXT", "src/Foo.cs", 42, "boom", "error")],
            new ScanSummary(1, 0, 0),
            new GateResult(true, "all", 1));

        var markdown = CommentFormatter.Render(result, previous: null);

        Assert.Contains("### `src/Foo.cs`", markdown);
        Assert.Contains("line 42", markdown);
    }

    private static ScanResult WithGate(GateResult gate, params Violation[] violations) => new(
        "1.0.0", "1.0.0+test", "/tmp/scan", DateTime.UtcNow, 12.3, 6, 7,
        violations, ScanSummary.From(violations), gate);

    private static Violation Error(string file, int line, bool baselined = false) =>
        new("PORTCULLIS_P9_CONTROLLER_NO_DBCONTEXT", file, line, "boom", "error", "fp-" + file + line, baselined);

    [Fact]
    public void Render_AcceptedViolation_IsListedAndMarked()
    {
        var markdown = CommentFormatter.Render(
            WithGate(new GateResult(false, "all", 0, AcceptedByBaselineCount: 1), Error("src/Foo.cs", 12, baselined: true)),
            previous: null);

        Assert.Contains("**line 12** — `PORTCULLIS_P9_CONTROLLER_NO_DBCONTEXT`: boom *(accepted by the baseline)*", markdown);
    }

    [Fact]
    public void Render_AllScopeWithEveryErrorAccepted_SaysWhyTheRedCountDoesNotBlock()
    {
        var markdown = CommentFormatter.Render(
            WithGate(new GateResult(false, "all", 0, AcceptedByBaselineCount: 2),
                Error("src/Foo.cs", 12, baselined: true), Error("src/Bar.cs", 3, baselined: true)),
            previous: null);

        Assert.Contains("ℹ️ Not blocking — the 2 errors above are accepted by the baseline.", markdown);
    }

    [Fact]
    public void Render_AllScopeBlockedWithSomeAccepted_CountsEachSeparately()
    {
        var markdown = CommentFormatter.Render(
            WithGate(new GateResult(true, "all", 1, AcceptedByBaselineCount: 1),
                Error("src/Foo.cs", 12, baselined: true), Error("src/Bar.cs", 3)),
            previous: null);

        Assert.Contains("🚫 **Blocking** — 1 error not in the baseline; 1 more is accepted by it.", markdown);
    }

    [Fact]
    public void Render_DiffScopeBlockedWithSomeAccepted_AddsTheAcceptedCount()
    {
        var markdown = CommentFormatter.Render(
            WithGate(new GateResult(true, "diff", 1, AcceptedByBaselineCount: 2),
                Error("src/Foo.cs", 12), Error("src/Bar.cs", 3, baselined: true), Error("src/Baz.cs", 4, baselined: true)),
            previous: null);

        Assert.Contains(
            "🚫 **Blocking this PR** — 1 error within this PR's own changed lines. 2 more there are accepted by the baseline.",
            markdown);
    }

    [Fact]
    public void Render_DiffScopeNotBlocked_SaysHowManyAreAcceptedAndHowManyAreOutsideTheDiff()
    {
        var markdown = CommentFormatter.Render(
            WithGate(new GateResult(false, "diff", 0, AcceptedByBaselineCount: 1),
                Error("src/Foo.cs", 12, baselined: true), Error("src/Bar.cs", 3), Error("src/Baz.cs", 4)),
            previous: null);

        Assert.Contains(
            "ℹ️ Not blocking this PR — of the 3 errors above, 1 is accepted by the baseline and 2 are outside this PR's own changed lines.",
            markdown);
    }

    [Fact]
    public void Render_DiffScopeNotBlockedWithEveryErrorAccepted_SaysSo()
    {
        var markdown = CommentFormatter.Render(
            WithGate(new GateResult(false, "diff", 0, AcceptedByBaselineCount: 1), Error("src/Foo.cs", 12, baselined: true)),
            previous: null);

        Assert.Contains("ℹ️ Not blocking this PR — the 1 error above is accepted by the baseline.", markdown);
    }

    [Fact]
    public void Render_DegradedGateWithAcceptedErrors_SaysTheBaselineStillApplies()
    {
        var markdown = CommentFormatter.Render(
            WithGate(new GateResult(true, "all", 1, "git could not resolve 'abc..def'", AcceptedByBaselineCount: 1),
                Error("src/Foo.cs", 12), Error("src/Bar.cs", 3, baselined: true)),
            previous: null);

        Assert.Contains("so pre-existing violations count here, except the 1 the baseline accepts.", markdown);
    }
}
