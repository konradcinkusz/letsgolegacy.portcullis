using Portcullis.Engine.Model;

namespace Portcullis.CiComment;

/// <summary>
/// New / resolved / unchanged violations between two scans of the same target.
/// Identity is full Violation value equality (ruleId + filePath + line + message +
/// severity) — a violation whose message or severity changed between pushes counts as
/// one resolved and one new rather than "the same" one, which is the honest reading
/// when nothing guarantees rule text is stable across engine versions.
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

        var previousSet = new HashSet<Violation>(previous);
        var currentSet = new HashSet<Violation>(current);

        var added = current.Where(v => !previousSet.Contains(v)).ToList();
        var resolved = previous.Where(v => !currentSet.Contains(v)).ToList();
        var unchanged = current.Where(v => previousSet.Contains(v)).ToList();

        return new ViolationDiffResult(added, resolved, unchanged);
    }
}
