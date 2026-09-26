using Portcullis.Engine.Model;

namespace Portcullis.CiComment;

/// <summary>
/// New / resolved / unchanged violations between two scans of the same target.
/// Identity is the five contract fields (ruleId + filePath + line + message + severity) —
/// a violation whose message or severity changed between pushes counts as one resolved
/// and one new rather than "the same" one, which is the honest reading when nothing
/// guarantees rule text is stable across engine versions.
///
/// <c>Fingerprint</c> and <c>Baselined</c> (ticket R3) are deliberately not part of that
/// identity, although record equality would include them. The previous push's scan comes
/// from a cache, possibly written by an engine that predates fingerprints, and a baseline
/// can be rewritten between two pushes; either would otherwise report every violation as
/// resolved and re-introduced in the same comment, with nothing in the code having changed.
/// </summary>
public sealed record ViolationDiffResult(
    IReadOnlyList<Violation> New,
    IReadOnlyList<Violation> Resolved,
    IReadOnlyList<Violation> Unchanged);

public static class ViolationDiff
{
    public static ViolationDiffResult Compute(IReadOnlyList<Violation> current, IReadOnlyList<Violation>? previous)
    {
        if (previous is null)
        {
            return new ViolationDiffResult(current.ToList(), Array.Empty<Violation>(), Array.Empty<Violation>());
        }

        var previousSet = new HashSet<(string, string, int, string, string)>(previous.Select(Identity));
        var currentSet = new HashSet<(string, string, int, string, string)>(current.Select(Identity));

        var added = current.Where(v => !previousSet.Contains(Identity(v))).ToList();
        var resolved = previous.Where(v => !currentSet.Contains(Identity(v))).ToList();
        var unchanged = current.Where(v => previousSet.Contains(Identity(v))).ToList();

        return new ViolationDiffResult(added, resolved, unchanged);
    }

    private static (string, string, int, string, string) Identity(Violation v) =>
        (v.RuleId, v.FilePath, v.Line, v.Message, v.Severity);
}
