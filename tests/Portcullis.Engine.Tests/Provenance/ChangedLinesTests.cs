using Portcullis.Engine.Model;
using Portcullis.Engine.Provenance;

namespace Portcullis.Engine.Tests.Provenance;

/// <summary>
/// The one answer to "is this violation on a changed line", shared by the diff-scoped gate,
/// severity escalation and the SARIF filter. The scanner-level tests exercise it through
/// the gate; these pin the edges directly.
/// </summary>
public class ChangedLinesTests
{
    private static readonly string RepoRoot = Path.Combine(Path.GetTempPath(), "portcullis-changed-lines", "repo");
    private static readonly string ScanRoot = Path.Combine(RepoRoot, "src");

    private static ProvenanceRange Range(string file, int start, int end, ProvenanceSource source = ProvenanceSource.Human) =>
        new(file, start, end, source, 1.0, new ProvenanceAttribution(null, "test", "deadbeef"));

    private static Violation At(string file, int line) => new("RULE", file, line, "m", "error");

    [Fact]
    public void Contains_ALineInsideARangeOfTheSameFile_ResolvingBothRootsToOneAbsolutePath()
    {
        // The report's paths are relative to the repository; the violation's to the
        // scanned subdirectory. Both have to land on the same file.
        var lines = ChangedLines.From(new ProvenanceReport("1.0.0", "a..b", [Range("src/Shop/Orders.cs", 10, 12)]), RepoRoot);

        Assert.True(lines.Contains(At("Shop/Orders.cs", 10), ScanRoot));
        Assert.True(lines.Contains(At("Shop/Orders.cs", 12), ScanRoot));
        Assert.False(lines.Contains(At("Shop/Orders.cs", 9), ScanRoot));
        Assert.False(lines.Contains(At("Shop/Orders.cs", 13), ScanRoot));
        Assert.False(lines.Contains(At("Shop/Invoices.cs", 10), ScanRoot));
    }

    [Fact]
    public void Contains_ARepositoryWideViolation_IsNeverOnAChangedLine()
    {
        var lines = ChangedLines.From(new ProvenanceReport("1.0.0", "a..b", [Range("src/Shop/Orders.cs", 1, 100)]), RepoRoot);

        Assert.False(lines.Contains(At("", 1), ScanRoot));
    }

    [Fact]
    public void From_ADegradedReport_IsRefusedRatherThanReadAsNoChangedLines()
    {
        // Scoping to a degraded report's empty range set is the fail-open gate that
        // ProvenanceStatus exists to prevent.
        var degraded = new ProvenanceReport("1.0.0", "a..b", [], ProvenanceStatus.Degraded, "git failed");

        Assert.Throws<ArgumentException>(() => ChangedLines.From(degraded, RepoRoot));
    }

    [Fact]
    public void From_AnEmptyCompleteReport_IsARealEmptyDiff()
    {
        var lines = ChangedLines.From(new ProvenanceReport("1.0.0", "a..b", []), RepoRoot);

        Assert.True(lines.IsEmpty);
        Assert.False(lines.Contains(At("Shop/Orders.cs", 1), ScanRoot));
    }

    [Fact]
    public void From_ASubsetOfRanges_ScopesToThatSubsetOnly()
    {
        IEnumerable<ProvenanceRange> ranges = [
            Range("src/Shop/Orders.cs", 1, 1, ProvenanceSource.Ai),
            Range("src/Shop/Orders.cs", 2, 2, ProvenanceSource.Human),
        ];

        var aiOnly = ChangedLines.From(ranges.Where(r => r.Source == ProvenanceSource.Ai), RepoRoot);

        Assert.True(aiOnly.Contains(At("Shop/Orders.cs", 1), ScanRoot));
        Assert.False(aiOnly.Contains(At("Shop/Orders.cs", 2), ScanRoot));
    }
}
