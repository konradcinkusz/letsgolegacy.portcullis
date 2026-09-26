using System.Text;
using System.Text.RegularExpressions;

namespace Portcullis.Engine.Provenance;

/// <summary>A contiguous span of lines a diff added or modified in the resulting file.</summary>
internal readonly record struct ChangedSpan(string FilePath, int StartLine, int EndLine);

/// <summary>One line of `git blame --porcelain` output, reduced to its final line number and commit.</summary>
internal readonly record struct BlamedLine(int Line, string CommitSha);

/// <param name="Spans">One entry per contiguous added/modified run the patch describes.</param>
/// <param name="SawUnattributedHunk">
/// True when at least one "@@" hunk arrived with no successfully-parsed "+++" header to
/// attribute it to — i.e. the parser was handed a shape it does not understand, and
/// silently dropped real changed lines.
///
/// This is reported rather than swallowed because it is the one diff-side failure git
/// itself does not signal: git exits 0, nothing throws, and the result is simply a
/// smaller span list. Under a diff-scoped gate a smaller span list means a more lenient
/// gate, so a parser that quietly loses a file is a parser that quietly stops enforcing —
/// the same fail-open shape <see cref="ProvenanceStatus"/> exists to close, arriving by a
/// different route.
///
/// Deliberately NOT inferred from "non-empty patch but zero spans", which is a real and
/// common *correct* outcome: a pure-deletion commit and a binary-only commit both produce
/// exactly that, and treating either as a failure would degrade the gate on ordinary,
/// well-formed diffs.
/// </param>
internal readonly record struct AddedSpanParse(
    IReadOnlyList<ChangedSpan> Spans,
    bool SawUnattributedHunk);

/// <summary>
/// Pure text parsers for the specific git plumbing shapes this provider reads: `git
/// diff -U0` hunk headers, `git blame --porcelain` line headers, and a `%(trailers)`
/// block. No git process invocation (see <see cref="GitCommandRunner"/>) and no
/// classification policy (see <see cref="AiToolClassifier"/>) here.
/// </summary>
internal static class GitOutputParsers
{
    private static readonly Regex NewFileSectionPattern = new(@"^diff --git ", RegexOptions.Compiled);

    // Two accepted forms for the "+++" line, because git emits both. The plain `b/<path>`
    // is the usual one. The quoted `"b/<path>"` is what git produces whenever the path
    // contains a double quote, a backslash, or a control character — unconditionally, and
    // regardless of the core.quotePath=false that GitCommandRunner pins (that option only
    // governs non-ASCII bytes). Treating the quoted form as unparseable made an ordinary
    // diff of an ordinarily-named-on-POSIX file degrade the whole gate to whole-tree
    // scope, which is the pre-existing-debt regression the diff gate exists to remove.
    private static readonly Regex FileHeaderPattern =
        new(@"^\+\+\+ (?:""b/(?<quoted>(?:[^""\\]|\\.)*)""|b/(?<path>.+)|/dev/null)\r?$",
            RegexOptions.Compiled);

    // "-U0" collapses every hunk to exactly the lines that were added or modified, so the
    // resulting spans are real changed-line identity, never a whole-file/whole-hunk guess.
    private static readonly Regex HunkHeaderPattern =
        new(@"^@@ -\d+(?:,\d+)? \+(?<start>\d+)(?:,(?<count>\d+))? @@", RegexOptions.Compiled);

    /// <summary>
    /// Extracts one span per contiguous added/modified run, from the "+" side of a
    /// unified diff. Pure deletions (a hunk whose new-side count is 0) are skipped: with
    /// nothing left in the resulting file, there is no line identity to attribute.
    /// Binary file sections contain no "@@" hunks at all and are skipped the same way.
    ///
    /// The two ways a hunk ends up with no file are tracked separately rather than
    /// collapsed, because they mean opposite things. A "+++ /dev/null" header is a
    /// deleted file: parsed fine, genuinely has no resulting lines, contributes no spans,
    /// and is completely normal. A header this parser could not read at all means real
    /// changed lines are being dropped — that one sets
    /// <see cref="AddedSpanParse.SawUnattributedHunk"/>. Before they were distinguished,
    /// both simply left <c>currentFile</c> null and vanished identically.
    /// </summary>
    public static AddedSpanParse ParseAddedSpans(string patchText)
    {
        var spans = new List<ChangedSpan>();
        string? currentFile = null;
        // Distinguishes "this section's +++ header parsed, and named /dev/null" (a
        // deletion — currentFile null, legitimately) from "no +++ header parsed for this
        // section at all" (currentFile also null, but because parsing failed).
        var currentHeaderParsed = false;
        var sawUnattributedHunk = false;

        foreach (var line in patchText.Split('\n'))
        {
            // A new "diff --git" section always starts a different file; clearing here
            // first means an unparseable "+++" line (an unanticipated quoting form, say)
            // can only cost that one file's spans, never leak the previous file's
            // identity onto hunks that do not belong to it.
            if (NewFileSectionPattern.IsMatch(line))
            {
                currentFile = null;
                currentHeaderParsed = false;
                continue;
            }

            var fileMatch = FileHeaderPattern.Match(line);
            if (fileMatch.Success)
            {
                currentFile =
                    fileMatch.Groups["quoted"].Success ? Unquote(fileMatch.Groups["quoted"].Value)
                    : fileMatch.Groups["path"].Success ? fileMatch.Groups["path"].Value
                    : null;
                currentHeaderParsed = true;
                continue;
            }

            var hunkMatch = HunkHeaderPattern.Match(line);
            if (!hunkMatch.Success)
                continue;

            if (!currentHeaderParsed)
            {
                sawUnattributedHunk = true;
                continue;
            }

            // Parsed header naming /dev/null: the file was deleted, so its hunks have no
            // resulting-file line identity. Nothing wrong happened.
            if (currentFile is null)
                continue;

            var count = hunkMatch.Groups["count"].Success ? int.Parse(hunkMatch.Groups["count"].Value) : 1;
            if (count == 0)
                continue;

            var start = int.Parse(hunkMatch.Groups["start"].Value);
            spans.Add(new ChangedSpan(currentFile, start, start + count - 1));
        }

        return new AddedSpanParse(spans, sawUnattributedHunk);
    }

    /// <summary>
    /// Reverses git's C-style quoting of a path: the escapes it emits are \" \\ and the
    /// usual control-character shorthands, plus three-digit octal for any other byte.
    ///
    /// Octal escapes are decoded as BYTES and then UTF-8 decoded together, not one char at
    /// a time — a non-ASCII character arrives as several octal escapes making up one
    /// multi-byte sequence, and decoding them individually would produce mojibake rather
    /// than the filename. (core.quotePath=false normally prevents non-ASCII quoting
    /// entirely, but this stays correct if that override is ever lost.)
    /// </summary>
    private static string Unquote(string quoted)
    {
        var bytes = new List<byte>(quoted.Length);
        for (var i = 0; i < quoted.Length; i++)
        {
            if (quoted[i] != '\\')
            {
                bytes.AddRange(Encoding.UTF8.GetBytes(quoted[i].ToString()));
                continue;
            }

            i++;
            if (i >= quoted.Length)
            {
                break;
            }

            switch (quoted[i])
            {
                case 'a': bytes.Add((byte)'\a'); break;
                case 'b': bytes.Add((byte)'\b'); break;
                case 'f': bytes.Add((byte)'\f'); break;
                case 'n': bytes.Add((byte)'\n'); break;
                case 'r': bytes.Add((byte)'\r'); break;
                case 't': bytes.Add((byte)'\t'); break;
                case 'v': bytes.Add((byte)'\v'); break;
                case >= '0' and <= '7' when i + 2 < quoted.Length:
                    bytes.Add(Convert.ToByte(quoted.Substring(i, 3), 8));
                    i += 2;
                    break;
                default: bytes.AddRange(Encoding.UTF8.GetBytes(quoted[i].ToString())); break;
            }
        }

        return Encoding.UTF8.GetString(bytes.ToArray());
    }

    // Matches only the "<sha> <origLine> <finalLine> [<numLines>]" header git emits once
    // per attributed line; blamed source-line content always starts with a tab and is
    // filtered out below, so it can never collide with this pattern.
    private static readonly Regex BlameLinePattern =
        new(@"^(?<sha>[0-9a-f]{40}) \d+ (?<final>\d+)(?: \d+)?$", RegexOptions.Compiled);

    public static IReadOnlyList<BlamedLine> ParseBlameLines(string porcelainText)
    {
        var lines = new List<BlamedLine>();
        foreach (var line in porcelainText.Split('\n'))
        {
            if (line.StartsWith('\t'))
                continue;

            var match = BlameLinePattern.Match(line);
            if (match.Success)
                lines.Add(new BlamedLine(int.Parse(match.Groups["final"].Value), match.Groups["sha"].Value));
        }

        return lines;
    }

    public static IReadOnlyList<(string Key, string Value)> ParseTrailers(string trailersBlock)
    {
        var trailers = new List<(string, string)>();
        foreach (var line in trailersBlock.Split('\n'))
        {
            if (string.IsNullOrWhiteSpace(line))
                continue;

            var colonIndex = line.IndexOf(':');
            if (colonIndex <= 0)
                continue;

            trailers.Add((line[..colonIndex].Trim(), line[(colonIndex + 1)..].Trim()));
        }

        return trailers;
    }
}
