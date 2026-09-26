using Portcullis.Engine.Findings;
using Portcullis.Engine.Model;

namespace Portcullis.Engine.Tests.Findings;

/// <summary>
/// The fingerprint every baseline on disk is written against. What it must survive (edits
/// elsewhere, re-indentation) and what it must not (a different rule, file, line text or
/// occurrence) are pinned here, and so is one exact value: a change to the hashing that
/// slipped through would silently un-accept every finding in every baseline.
/// </summary>
public class FindingFingerprintTests
{
    private const string Rule = "PORTCULLIS_MIG_SYNC_OVER_ASYNC";
    private const string FilePath = "Shop/Orders.cs";
    private const string Line = "return task.Result;";

    [Fact]
    public void Compute_MatchesTheValueComputedIndependently()
    {
        // sha256("portcullisFingerprint/v1\nPORTCULLIS_MIG_SYNC_OVER_ASYNC\nShop/Orders.cs\nreturn task.Result;\n0"),
        // computed outside .NET (Python's hashlib) when the scheme was written.
        Assert.Equal(
            "fa5f120f7522d562916755c5436ccdbaeaefac24f1f8fa7500f21924c2682c9e",
            FindingFingerprint.Compute(Rule, FilePath, Line, 0));
    }

    [Fact]
    public void Compute_IgnoresIndentationAndInnerWhitespace()
    {
        var reference = FindingFingerprint.Compute(Rule, FilePath, Line, 0);

        Assert.Equal(reference, FindingFingerprint.Compute(Rule, FilePath, "        return task.Result;", 0));
        Assert.Equal(reference, FindingFingerprint.Compute(Rule, FilePath, "\treturn   task.Result;  \r", 0));
    }

    [Fact]
    public void Compute_ChangesWithEachOfItsInputs()
    {
        var reference = FindingFingerprint.Compute(Rule, FilePath, Line, 0);

        var variants = new[]
        {
            FindingFingerprint.Compute("PORTCULLIS_MIG_SYSTEM_WEB", FilePath, Line, 0),
            FindingFingerprint.Compute(Rule, "Shop/Invoices.cs", Line, 0),
            FindingFingerprint.Compute(Rule, FilePath, "return other.Result;", 0),
            FindingFingerprint.Compute(Rule, FilePath, Line, 1),
        };

        Assert.DoesNotContain(reference, variants);
        Assert.Equal(variants.Length, variants.Distinct().Count());
    }

    [Fact]
    public void Compute_IsLowercaseHexOfASha256()
    {
        var fingerprint = FindingFingerprint.Compute(Rule, FilePath, Line, 0);

        Assert.Matches("^[0-9a-f]{64}$", fingerprint);
    }

    [Fact]
    public void Assign_NumbersIdenticalFindingsByOccurrenceAndLeavesTheOthersAlone()
    {
        var first = new Violation(Rule, FilePath, 10, "message", "warning");
        var copy = new Violation(Rule, FilePath, 20, "message", "warning");
        var other = new Violation(Rule, FilePath, 30, "message", "warning");

        var assigned = FindingFingerprint.Assign([(first, Line), (copy, "  " + Line), (other, "return x.Result;")]);

        Assert.Equal(FindingFingerprint.Compute(Rule, FilePath, Line, 0), assigned[0].Fingerprint);
        Assert.Equal(FindingFingerprint.Compute(Rule, FilePath, Line, 1), assigned[1].Fingerprint);
        Assert.Equal(FindingFingerprint.Compute(Rule, FilePath, "return x.Result;", 0), assigned[2].Fingerprint);
        Assert.Equal(new[] { 10, 20, 30 }, assigned.Select(v => v.Line));
    }
}
