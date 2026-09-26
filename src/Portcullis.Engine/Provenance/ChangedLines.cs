using Portcullis.Engine.Model;

namespace Portcullis.Engine.Provenance;

/// <summary>
/// The lines a <see cref="ProvenanceReport"/> says a diff changed, resolved to absolute paths,
/// answering one question: does this violation sit on one of them?
///
/// It is the single answer to that question. The diff-scoped gate asks it to decide what may
/// block (<see cref="Scanner"/>), severity escalation asks it about AI-authored ranges only,
/// and the SARIF changed-lines filter asks it to decide what a pull request's SARIF contains
/// (docs/SARIF.md). Before it existed the check lived as private helpers inside the scanner;
/// a second copy for SARIF would have been free to drift from the gate's, and a SARIF file
/// that disagreed with the gate about which findings belong to a pull request would make one
/// of the two wrong without anything saying which.
/// </summary>
public sealed class ChangedLines
{
    private readonly List<(string Path, int StartLine, int EndLine)> _ranges;

    private ChangedLines(List<(string Path, int StartLine, int EndLine)> ranges) => _ranges = ranges;

    /// <summary>Whether no line changed at all — a real, empty diff, since a degraded report is refused.</summary>
    public bool IsEmpty => _ranges.Count == 0;

    /// <summary>
    /// Every range in <paramref name="report"/>, whoever wrote it: whether a line was part of
    /// the diff does not depend on its author.
    ///
    /// A <see cref="ProvenanceStatus.Degraded"/> report is refused rather than read. Its ranges
    /// are not an answer, and scoping to them — to nothing — is the fail-open gate
    /// <see cref="ProvenanceStatus"/> exists to prevent (docs/DIFF-GATE.md section 3).
    /// </summary>
    /// <param name="report">A complete provenance report.</param>
    /// <param name="provenanceRoot">The directory the report's file paths are relative to: the repository's top level.</param>
    public static ChangedLines From(ProvenanceReport report, string provenanceRoot)
    {
        if (report.Status == ProvenanceStatus.Degraded)
        {
            throw new ArgumentException(
                "A degraded provenance report has no changed-line set to scope by; fall back to the whole scan instead.",
                nameof(report));
        }

        return From(report.Ranges, provenanceRoot);
    }

    /// <summary>Exactly the given ranges — used for the AI-authored subset that drives escalation.</summary>
    public static ChangedLines From(IEnumerable<ProvenanceRange> ranges, string provenanceRoot) =>
        new(ranges.Select(r => (ToAbsolutePath(provenanceRoot, r.FilePath), r.StartLine, r.EndLine)).ToList());

    /// <summary>
    /// Whether <paramref name="violation"/> is on a changed line. A violation with no file (a
    /// repository-wide one) is on no line, so it never is.
    /// </summary>
    /// <param name="violation">A violation from a scan of <paramref name="scanRoot"/>.</param>
    /// <param name="scanRoot">The directory the violation's file path is relative to: the scanned path.</param>
    public bool Contains(Violation violation, string scanRoot)
    {
        if (string.IsNullOrEmpty(violation.FilePath))
        {
            return false;
        }

        var violationPath = ToAbsolutePath(scanRoot, violation.FilePath);
        return _ranges.Any(r =>
            string.Equals(r.Path, violationPath, StringComparison.OrdinalIgnoreCase)
            && violation.Line >= r.StartLine
            && violation.Line <= r.EndLine);
    }

    private static string ToAbsolutePath(string root, string relativePath) =>
        Path.GetFullPath(Path.Combine(root, relativePath.Replace('/', Path.DirectorySeparatorChar)));
}
