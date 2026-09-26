using System.Text.RegularExpressions;

namespace Portcullis.Engine.Provenance;

/// <summary>
/// Implements <see cref="IProvenanceProvider"/> by reading git history directly — never
/// code style (docs/SPEC.md section 4). For a commit or
/// diff range, this resolves exactly which lines it changed (`git diff -U0`, so hunks
/// already equal real changed-line spans), then asks `git blame` which commit most
/// recently touched each of those lines, then classifies that commit from its trailers
/// and author identity (<see cref="AiToolClassifier"/>). Routing through blame — rather
/// than trusting each commit's own diff at face value — means a multi-commit range
/// correctly attributes to whichever commit's change survived, not every commit that
/// ever touched the range.
///
/// Every emitted <see cref="ProvenanceRange"/> carries a real FilePath/StartLine/EndLine
/// and an explicit Source/Tool — including <see cref="ProvenanceSource.Unknown"/> with a
/// null Tool when a range's commit genuinely cannot be attributed — never a session- or
/// commit-wide average with no line identity. That is the specific copilot-scope failure
/// mode (docs/SPEC.md section 4) this interface exists to not repeat.
/// </summary>
public sealed class GitProvenanceProvider(string repositoryPath) : IProvenanceProvider
{
    private const string SchemaVersion = "1.0.0";

    // The well-known empty tree object every git repository has, used as the diff base
    // for a root commit (one with no parent) so "everything in it" is reported as added.
    private const string EmptyTreeSha = "4b825dc642cb6eb9a060e54bf8d69288fbee4904";

    private static readonly Regex RangeDotsPattern = new(@"\.\.\.?", RegexOptions.Compiled);

    public ProvenanceReport GetProvenance(string commitOrRange)
    {
        AddedSpanParse parse;
        string tip;
        try
        {
            string diffBase;
            (diffBase, tip) = ResolveBaseAndTip(commitOrRange);
            var patch = GitCommandRunner.Run(repositoryPath, "diff", "--no-color", "-U0", diffBase, tip);
            parse = GitOutputParsers.ParseAddedSpans(patch);
        }
        catch (GitCommandException ex)
        {
            // commitOrRange (or repositoryPath itself) does not resolve to a real git
            // range. This used to return an ordinary empty report — indistinguishable
            // from a diff that genuinely changed nothing, which is what made the
            // diff-scoped gate pass on a broken checkout. Reported as Degraded now, so
            // Scanner.ScanAsync can fall back to absolute gating instead of scoping to a
            // range set that does not exist. See ProvenanceStatus for the full reasoning.
            return Degraded(commitOrRange, $"git could not resolve '{commitOrRange}' in '{repositoryPath}': {ex.Message}");
        }

        if (parse.SawUnattributedHunk)
        {
            // git succeeded and exited 0, but this parser could not attribute at least one
            // hunk to a file, so the span list is missing real changed lines. Silently
            // narrowing a diff-scoped gate is the same fail-open outcome as an outright
            // git failure, so it gets the same treatment rather than a quieter one.
            return Degraded(
                commitOrRange,
                $"the diff for '{commitOrRange}' contained a hunk with no parseable file header, " +
                "so the changed-line set is incomplete");
        }

        var ranges = new List<ProvenanceRange>();
        var classificationCache = new Dictionary<string, AiToolClassification>();

        foreach (var span in parse.Spans)
        {
            IReadOnlyList<BlamedLine> blameLines;
            try
            {
                var blameText = GitCommandRunner.Run(
                    repositoryPath, "blame", "--porcelain", "-L", $"{span.StartLine},{span.EndLine}",
                    tip, "--", span.FilePath);
                blameLines = GitOutputParsers.ParseBlameLines(blameText);
            }
            catch (GitCommandException)
            {
                ranges.Add(UnknownRange(span));
                continue;
            }

            if (blameLines.Count == 0)
            {
                ranges.Add(UnknownRange(span));
                continue;
            }

            foreach (var (sha, startLine, endLine) in GroupConsecutiveSameCommit(blameLines))
            {
                if (!classificationCache.TryGetValue(sha, out var classification))
                {
                    classification = ClassifyCommit(sha);
                    classificationCache[sha] = classification;
                }

                ranges.Add(new ProvenanceRange(
                    span.FilePath, startLine, endLine,
                    classification.Source, classification.Confidence,
                    new ProvenanceAttribution(classification.Tool, classification.Method, sha)));
            }
        }

        var ordered = ranges
            .OrderBy(r => r.FilePath, StringComparer.Ordinal)
            .ThenBy(r => r.StartLine)
            .ToList();

        return new ProvenanceReport(SchemaVersion, commitOrRange, ordered);
    }

    // A report whose Ranges must not be used to scope anything. Ranges is deliberately
    // left empty: a degraded report has no answer to give, and handing back a partial one
    // would invite a caller to scope by it anyway.
    private static ProvenanceReport Degraded(string commitOrRange, string reason) =>
        new(SchemaVersion, commitOrRange, [], ProvenanceStatus.Degraded, SingleLine(reason));

    // git writes multi-line advice to stderr ("fatal: ambiguous argument ...\nUse '--' to
    // separate paths ..."), and this string is rendered verbatim into a Markdown PR
    // comment, where embedded newlines break out of the sentence they belong to. Collapsed
    // rather than truncated: the advice is genuinely useful to whoever has to fix the
    // checkout, so none of it is thrown away.
    private static readonly char[] LineBreaks = ['\r', '\n'];

    private static string SingleLine(string text) =>
        string.Join(" ", text.Split(LineBreaks, StringSplitOptions.RemoveEmptyEntries)
            .Select(line => line.Trim()));

    // A rule of the provenance design (docs/SPEC.md section 4): a range whose tool cannot
    // be determined is reported explicitly as Unknown/null rather than guessed at or
    // silently dropped.
    private static ProvenanceRange UnknownRange(ChangedSpan span) => new(
        span.FilePath, span.StartLine, span.EndLine,
        ProvenanceSource.Unknown, 0.0,
        new ProvenanceAttribution(null, "no-attribution-signal", null));

    // The same "no attribution signal" answer UnknownRange gives, reached by a different
    // route: blame named a commit, but its metadata could not be read.
    private static readonly AiToolClassification UnclassifiableCommit =
        new(ProvenanceSource.Unknown, 0.0, null, "no-attribution-signal");

    /// <summary>
    /// Reads one commit's author identity and trailers, and hands them to
    /// <see cref="AiToolClassifier"/>.
    ///
    /// The try/catch matters more than it looks: this is called from inside the span loop
    /// but outside both of that loop's try blocks, so before it was guarded a
    /// <see cref="GitCommandException"/> here propagated straight out of
    /// <see cref="GetProvenance"/> and killed the process — while its sibling `git diff`
    /// failing went silently green. One fault class, two opposite outcomes, neither of
    /// them the defined one.
    ///
    /// A failure here does NOT degrade the report, and that asymmetry is deliberate: the
    /// changed-line set is already known and correct at this point, so the gate can still
    /// scope to it honestly. All that is lost is who wrote those lines, which only feeds
    /// severity escalation — and escalation can only ever make a violation stricter, so
    /// losing it never turns a blocking violation into a passing one. Degrading here
    /// would instead flip the whole scan to absolute gating over one unreadable commit,
    /// re-blocking PRs on pre-existing debt — the exact regression M5A's diff-scoped gate
    /// existed to remove (docs/FINDINGS.md).
    /// </summary>
    private AiToolClassification ClassifyCommit(string sha)
    {
        string raw;
        try
        {
            raw = GitCommandRunner.Run(
                repositoryPath, "log", "-1", "--format=%an%x09%ae%x00%(trailers:only=true,unfold=true)", sha);
        }
        catch (GitCommandException)
        {
            return UnclassifiableCommit;
        }

        var headerAndTrailers = raw.Split('\x00', 2);
        var authorFields = headerAndTrailers[0].Split('\t', 2);
        var authorName = authorFields[0];
        var authorEmail = authorFields.Length > 1 ? authorFields[1].Trim() : "";
        var trailersBlock = headerAndTrailers.Length > 1 ? headerAndTrailers[1] : "";

        var trailers = GitOutputParsers.ParseTrailers(trailersBlock);
        return AiToolClassifier.Classify(authorName, authorEmail, trailers);
    }

    /// <summary>
    /// A bare commit is treated as the range "&lt;parent&gt;..&lt;commit&gt;" (or the
    /// empty tree for a root commit) — exactly what that one commit changed. A "A..B" or
    /// "A...B" input is diffed with plain two-ref (direct) semantics in both cases; true
    /// triple-dot merge-base semantics are not attempted, a deliberate simplification.
    ///
    /// For a bare merge commit specifically, "parent" means the first parent only
    /// (mainline) — the same convention GitHub's own PR view uses for "what this merge
    /// introduced". Lines that entered solely via a non-first parent (and were kept as-is
    /// by the merge) get no range from this call; querying that content directly against
    /// the second parent's own history is the workaround, not attempted automatically.
    /// </summary>
    private (string DiffBase, string Tip) ResolveBaseAndTip(string commitOrRange)
    {
        var dotsMatch = RangeDotsPattern.Match(commitOrRange);
        if (dotsMatch.Success)
        {
            var left = commitOrRange[..dotsMatch.Index].Trim();
            var right = commitOrRange[(dotsMatch.Index + dotsMatch.Length)..].Trim();
            return (left, right);
        }

        var sha = GitCommandRunner.Run(repositoryPath, "rev-parse", "--verify", commitOrRange).Trim();
        var parents = GitCommandRunner.Run(repositoryPath, "rev-list", "--parents", "-n", "1", sha)
            .Trim().Split(' ');
        var diffBase = parents.Length > 1 ? parents[1] : EmptyTreeSha;
        return (diffBase, sha);
    }

    private static IEnumerable<(string Sha, int StartLine, int EndLine)> GroupConsecutiveSameCommit(
        IReadOnlyList<BlamedLine> lines)
    {
        var index = 0;
        while (index < lines.Count)
        {
            var sha = lines[index].CommitSha;
            var start = lines[index].Line;
            var end = start;
            var next = index + 1;
            while (next < lines.Count && lines[next].CommitSha == sha && lines[next].Line == end + 1)
            {
                end = lines[next].Line;
                next++;
            }

            yield return (sha, start, end);
            index = next;
        }
    }
}
