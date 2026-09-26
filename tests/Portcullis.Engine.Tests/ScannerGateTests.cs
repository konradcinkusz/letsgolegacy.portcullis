using Portcullis.Engine.Provenance;

namespace Portcullis.Engine.Tests;

/// <summary>
/// Covers the diff-scoped merge gate added after M5's docs/FINDINGS.md reproduced the
/// absolute-gate gap on demand: Gate.Scope is "all" (today's original, unconditional
/// behavior) when no provenance range is given, or "diff" when one is — in which case
/// only error-severity violations inside that range's changed lines count toward
/// Gate.Blocked/BlockingErrorCount, regardless of who authored them (unlike escalation,
/// covered separately in ScannerProvenanceTests.cs, which cares only about Ai-sourced
/// ranges). Exercised against the real, registered rules, mirroring
/// ScannerProvenanceTests.cs's own style.
/// </summary>
public class ScannerGateTests
{
    // Triggers PORTCULLIS_P9_CONTROLLER_NO_DBCONTEXT, default severity "error".
    private const string SourceWithErrorViolation = """
        namespace Demo;

        public class FooDbContext : DbContext
        {
        }

        [ApiController]
        public class FooController : ControllerBase
        {
            private readonly FooDbContext _db;

            public FooController(FooDbContext db)
            {
                _db = db;
            }
        }
        """;

    // Triggers PORTCULLIS_P10_CUSTOM_BASE_CLASS, default severity "warning".
    private const string SourceWithWarningViolation =
        "namespace Sample;\n\npublic class Base { }\npublic class Derived : Base { }\n";

    private static ProvenanceReport RangeReport(string filePath, int line, ProvenanceSource source) =>
        new("1.0.0", "test", [
            new ProvenanceRange(filePath, line, line, source, 0.9, new ProvenanceAttribution("test", "test", "deadbeef")),
        ]);

    [Fact]
    public async Task ScanAsync_NoProvenanceGiven_GateScopeIsAllAndMatchesTheAbsoluteErrorCount()
    {
        var tempDir = Directory.CreateTempSubdirectory("portcullis-gate-test-");
        try
        {
            await File.WriteAllTextAsync(Path.Combine(tempDir.FullName, "Foo.cs"), SourceWithErrorViolation);

            var result = await Scanner.ScanAsync(tempDir.FullName);

            Assert.NotNull(result.Gate);
            Assert.Equal("all", result.Gate.Scope);
            Assert.True(result.Gate.Blocked);
            Assert.Equal(result.Summary.ErrorCount, result.Gate.BlockingErrorCount);
        }
        finally
        {
            tempDir.Delete(recursive: true);
        }
    }

    [Fact]
    public async Task ScanAsync_ErrorViolationOutsideTheProvenanceRange_DoesNotBlockEvenThoughAnErrorExists()
    {
        var tempDir = Directory.CreateTempSubdirectory("portcullis-gate-test-");
        try
        {
            await File.WriteAllTextAsync(Path.Combine(tempDir.FullName, "Foo.cs"), SourceWithErrorViolation);

            var baseline = await Scanner.ScanAsync(tempDir.FullName);
            var violation = Assert.Single(baseline.Violations, v => v.RuleId == "PORTCULLIS_P9_CONTROLLER_NO_DBCONTEXT");

            // A range on an unrelated file — simulates a PR whose own diff never
            // touches the file carrying the pre-existing error.
            var provenance = RangeReport("SomeOtherFile.cs", 1, ProvenanceSource.Human);
            var result = await Scanner.ScanAsync(tempDir.FullName, provenance, tempDir.FullName);

            Assert.Equal(1, result.Summary.ErrorCount); // still reported, for visibility
            Assert.NotNull(result.Gate);
            Assert.Equal("diff", result.Gate.Scope);
            Assert.False(result.Gate.Blocked); // but does not block
            Assert.Equal(0, result.Gate.BlockingErrorCount);
        }
        finally
        {
            tempDir.Delete(recursive: true);
        }
    }

    [Fact]
    public async Task ScanAsync_ErrorViolationInsideTheProvenanceRange_Blocks()
    {
        var tempDir = Directory.CreateTempSubdirectory("portcullis-gate-test-");
        try
        {
            await File.WriteAllTextAsync(Path.Combine(tempDir.FullName, "Foo.cs"), SourceWithErrorViolation);

            var baseline = await Scanner.ScanAsync(tempDir.FullName);
            var violation = Assert.Single(baseline.Violations, v => v.RuleId == "PORTCULLIS_P9_CONTROLLER_NO_DBCONTEXT");

            var provenance = RangeReport(violation.FilePath, violation.Line, ProvenanceSource.Human);
            var result = await Scanner.ScanAsync(tempDir.FullName, provenance, tempDir.FullName);

            Assert.NotNull(result.Gate);
            Assert.Equal("diff", result.Gate.Scope);
            Assert.True(result.Gate.Blocked);
            Assert.Equal(1, result.Gate.BlockingErrorCount);
        }
        finally
        {
            tempDir.Delete(recursive: true);
        }
    }

    [Fact]
    public async Task ScanAsync_WarningEscalatedByAnAiRange_AlsoBlocksTheDiffScopedGate()
    {
        var tempDir = Directory.CreateTempSubdirectory("portcullis-gate-test-");
        try
        {
            await File.WriteAllTextAsync(Path.Combine(tempDir.FullName, "Sample.cs"), SourceWithWarningViolation);

            var baseline = await Scanner.ScanAsync(tempDir.FullName);
            var violation = Assert.Single(baseline.OfPrinciples());
            Assert.Equal("warning", violation.Severity);

            var provenance = RangeReport(violation.FilePath, violation.Line, ProvenanceSource.Ai);
            var result = await Scanner.ScanAsync(tempDir.FullName, provenance, tempDir.FullName);

            var escalated = Assert.Single(result.OfPrinciples());
            Assert.Equal("error", escalated.Severity); // M4 escalation, unchanged
            Assert.NotNull(result.Gate);
            Assert.Equal("diff", result.Gate.Scope);
            Assert.True(result.Gate.Blocked); // and now also blocks, composing correctly
            Assert.Equal(1, result.Gate.BlockingErrorCount);
        }
        finally
        {
            tempDir.Delete(recursive: true);
        }
    }

    [Fact]
    public async Task ScanAsync_HumanSourcedRangeOverAnError_StillBlocks_GatingIsSourceAgnostic()
    {
        // Unlike escalation (Ai-only), gating asks only "was this line in the given
        // range at all" — a human-authored new error in the PR's own diff must still
        // block, with no provenance/AI angle needed.
        var tempDir = Directory.CreateTempSubdirectory("portcullis-gate-test-");
        try
        {
            await File.WriteAllTextAsync(Path.Combine(tempDir.FullName, "Foo.cs"), SourceWithErrorViolation);

            var baseline = await Scanner.ScanAsync(tempDir.FullName);
            var violation = Assert.Single(baseline.Violations, v => v.RuleId == "PORTCULLIS_P9_CONTROLLER_NO_DBCONTEXT");

            var provenance = RangeReport(violation.FilePath, violation.Line, ProvenanceSource.Human);
            var result = await Scanner.ScanAsync(tempDir.FullName, provenance, tempDir.FullName);

            var stillError = Assert.Single(result.Violations, v => v.RuleId == "PORTCULLIS_P9_CONTROLLER_NO_DBCONTEXT");
            Assert.Equal("error", stillError.Severity); // untouched by escalation (already error)
            Assert.True(result.Gate!.Blocked); // but still blocks — gating doesn't care about source
        }
        finally
        {
            tempDir.Delete(recursive: true);
        }
    }

    [Fact]
    public async Task ScanAsync_ProvenanceGivenButNoRangesAtAll_DiffScopedAndNotBlocked()
    {
        // The clean-PR case: a range was resolved (e.g. a real, empty PR diff, or one
        // touching only non-.cs files) but produced zero ProvenanceRanges. Pre-existing
        // errors elsewhere must not block — the exact scenario 4 of the M5 scratch-repo
        // pass (docs/FINDINGS.md) demonstrated end-to-end.
        var tempDir = Directory.CreateTempSubdirectory("portcullis-gate-test-");
        try
        {
            await File.WriteAllTextAsync(Path.Combine(tempDir.FullName, "Foo.cs"), SourceWithErrorViolation);

            var provenance = new ProvenanceReport("1.0.0", "test", []);
            var result = await Scanner.ScanAsync(tempDir.FullName, provenance, tempDir.FullName);

            Assert.Equal(1, result.Summary.ErrorCount);
            Assert.NotNull(result.Gate);
            Assert.Equal("diff", result.Gate.Scope);
            Assert.False(result.Gate.Blocked);
            Assert.Equal(0, result.Gate.BlockingErrorCount);
        }
        finally
        {
            tempDir.Delete(recursive: true);
        }
    }

    // ---------------------------------------------------------------------------------
    // The fail-safe contract. A degraded provenance report is the shape a broken git
    // produces, and before this the scan scoped to its (empty) ranges and blocked on
    // nothing — a green gate over a tree full of errors. See ProvenanceStatus.
    // ---------------------------------------------------------------------------------

    private static ProvenanceReport DegradedReport(string reason) =>
        new("1.0.0", "test", [], ProvenanceStatus.Degraded, reason);

    [Fact]
    public async Task ScanAsync_DegradedProvenance_FallsBackToAbsoluteGateAndStillBlocks()
    {
        var tempDir = Directory.CreateTempSubdirectory("portcullis-gate-degraded-");
        try
        {
            await File.WriteAllTextAsync(Path.Combine(tempDir.FullName, "Foo.cs"), SourceWithErrorViolation);

            var result = await Scanner.ScanAsync(
                tempDir.FullName, DegradedReport("git could not resolve 'HEAD~1..HEAD'"));

            Assert.NotNull(result.Gate);
            Assert.True(result.Gate.Blocked);
            Assert.Equal("all", result.Gate.Scope);
            Assert.Equal(result.Summary.ErrorCount, result.Gate.BlockingErrorCount);
            Assert.Equal("git could not resolve 'HEAD~1..HEAD'", result.Gate.DegradedReason);
        }
        finally
        {
            tempDir.Delete(recursive: true);
        }
    }

    [Fact]
    public async Task ScanAsync_CompleteButEmptyProvenance_StillScopesToDiffAndPasses()
    {
        // The control case that makes the test above meaningful. An honestly empty range
        // set must keep its diff scope and keep passing — otherwise the fix would just be
        // "always gate on everything", which is the M5A regression, not a fix.
        var tempDir = Directory.CreateTempSubdirectory("portcullis-gate-empty-");
        try
        {
            await File.WriteAllTextAsync(Path.Combine(tempDir.FullName, "Foo.cs"), SourceWithErrorViolation);

            var result = await Scanner.ScanAsync(tempDir.FullName, new ProvenanceReport("1.0.0", "test", []));

            Assert.NotNull(result.Gate);
            Assert.False(result.Gate.Blocked);
            Assert.Equal("diff", result.Gate.Scope);
            Assert.Equal(0, result.Gate.BlockingErrorCount);
            Assert.Null(result.Gate.DegradedReason);
            Assert.True(result.Summary.ErrorCount > 0);
        }
        finally
        {
            tempDir.Delete(recursive: true);
        }
    }

    [Fact]
    public async Task ScanAsync_DegradedProvenanceOnACleanTree_StillPasses()
    {
        // Failing safe must not mean failing always: with nothing to report, a degraded
        // report still produces a passing gate. It carries the reason so the operator can
        // see the scope was not the one they asked for.
        var tempDir = Directory.CreateTempSubdirectory("portcullis-gate-degraded-clean-");
        try
        {
            await File.WriteAllTextAsync(
                Path.Combine(tempDir.FullName, "Clean.cs"), "namespace Clean;\n\npublic class Fine { }\n");

            var result = await Scanner.ScanAsync(tempDir.FullName, DegradedReport("git is unavailable"));

            Assert.NotNull(result.Gate);
            Assert.False(result.Gate.Blocked);
            Assert.Equal("all", result.Gate.Scope);
            Assert.Equal("git is unavailable", result.Gate.DegradedReason);
        }
        finally
        {
            tempDir.Delete(recursive: true);
        }
    }

    [Fact]
    public async Task ScanAsync_NoProvenanceGiven_LeavesDegradedReasonNull()
    {
        // "scope: all" must stay distinguishable from "scope: all because git broke".
        var tempDir = Directory.CreateTempSubdirectory("portcullis-gate-plain-");
        try
        {
            await File.WriteAllTextAsync(Path.Combine(tempDir.FullName, "Foo.cs"), SourceWithErrorViolation);

            var result = await Scanner.ScanAsync(tempDir.FullName);

            Assert.NotNull(result.Gate);
            Assert.Equal("all", result.Gate.Scope);
            Assert.Null(result.Gate.DegradedReason);
        }
        finally
        {
            tempDir.Delete(recursive: true);
        }
    }
}
