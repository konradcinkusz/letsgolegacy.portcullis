using System.Text;
using Portcullis.Engine.Model;

namespace Portcullis.CiComment;

/// <summary>
/// Renders a ScanResult (and, optionally, the previous push's ScanResult) into the
/// Markdown body of the sticky PR comment. Pure — no I/O, no GitHub API — so it is
/// testable entirely against fixtures/mock-engine-output.
///
/// Provenance never appears here: by a design decision fixed before any code was written
/// (docs/SPEC.md section 4), "AI-authored" is not a badge or score this comment
/// surfaces — it only ever changes which severity threshold a violation gets, upstream
/// of this formatter, which never sees that signal at all.
/// </summary>
public static class CommentFormatter
{
    public static string Render(ScanResult current, ScanResult? previous)
    {
        var diff = ViolationDiff.Compute(current.Violations, previous?.Violations);
        var sb = new StringBuilder();

        sb.AppendLine(StickyComment.Marker);
        sb.AppendLine("## Portcullis");
        sb.AppendLine();
        sb.AppendLine(RenderHeadline(current));

        var gateNote = RenderGateNote(current);
        if (gateNote is not null)
        {
            sb.AppendLine();
            sb.AppendLine(gateNote);
        }

        if (previous is not null && (diff.New.Count > 0 || diff.Resolved.Count > 0))
        {
            sb.AppendLine();
            sb.AppendLine($"🆕 {diff.New.Count} new · ✅ {diff.Resolved.Count} resolved since the last push.");

            if (diff.New.Count > 0)
            {
                sb.AppendLine();
                sb.AppendLine("**New:**");
                foreach (var v in Sorted(diff.New))
                {
                    sb.AppendLine($"- {SeverityIcon(v.Severity)} {LocationOf(v)} — `{v.RuleId}`: {v.Message}");
                }
            }

            if (diff.Resolved.Count > 0)
            {
                sb.AppendLine();
                sb.AppendLine("**Resolved:**");
                foreach (var v in Sorted(diff.Resolved))
                {
                    sb.AppendLine($"- ~~{LocationOf(v)} — `{v.RuleId}`~~");
                }
            }
        }

        if (current.Violations.Count > 0)
        {
            foreach (var group in current.Violations
                         .GroupBy(v => v.FilePath)
                         .OrderBy(g => g.Key, StringComparer.Ordinal))
            {
                sb.AppendLine();
                // A location-less diagnostic (ConventionCoverageAnalyzer reports against
                // the whole compilation) arrives with an empty FilePath, which would
                // otherwise render as an empty `` heading followed by a meaningless
                // "line 1".
                sb.AppendLine(string.IsNullOrEmpty(group.Key)
                    ? "### Repository-wide"
                    : $"### `{group.Key}`");
                foreach (var v in group.OrderBy(v => v.Line))
                {
                    sb.AppendLine(string.IsNullOrEmpty(group.Key)
                        ? $"- {SeverityIcon(v.Severity)} `{v.RuleId}`: {v.Message}"
                        : $"- {SeverityIcon(v.Severity)} **line {v.Line}** — `{v.RuleId}`: {v.Message}");
                }
            }
        }

        sb.AppendLine();
        sb.AppendLine($"<sub>portcullis · updated {current.ScanStartedUtc:yyyy-MM-ddTHH:mm:ssZ}</sub>");

        return sb.ToString();
    }

    private static string RenderHeadline(ScanResult result)
    {
        var stats = $"{result.RulesEvaluated} {Plural(result.RulesEvaluated, "rule")} evaluated across " +
                    $"{result.FilesScanned} {Plural(result.FilesScanned, "file")} in {result.ScanDurationMs:F0}ms.";

        if (result.RulesEvaluated == 0)
        {
            return $"⚪ 0 rules evaluated — nothing to check yet. ({stats})";
        }

        var s = result.Summary;
        if (s.ErrorCount == 0 && s.WarningCount == 0 && s.InfoCount == 0)
        {
            return $"✅ No architecture violations found. ({stats})";
        }

        var parts = new List<string>();
        if (s.ErrorCount > 0)
        {
            parts.Add($"❌ **{s.ErrorCount} {Plural(s.ErrorCount, "error")}**");
        }

        if (s.WarningCount > 0)
        {
            parts.Add($"⚠️ **{s.WarningCount} {Plural(s.WarningCount, "warning")}**");
        }

        if (s.InfoCount > 0)
        {
            parts.Add($"ℹ️ **{s.InfoCount} info**");
        }

        return $"{string.Join(", ", parts)} — {stats}";
    }

    // Only present when the scan was diff-scoped (Gate.Scope == "diff"); the original
    // "all"-scope / no-Gate rendering is completely unchanged, since the headline's
    // error/warning counts already ARE the gate decision in that case, with nothing more
    // to disambiguate. Diff scope is different: the headline still shows every violation
    // for visibility, so without this line a reader can't tell which ones are why the
    // build is red — or, just as important, that a red-looking count of errors did NOT
    // fail the build because none of them touch this PR's own changes.
    private static string? RenderGateNote(ScanResult result)
    {
        // A gate that fell back from diff scope because git failed is the one case where
        // the scope shown is not the scope requested. Saying so matters more than any
        // other note here: without it a reviewer sees pre-existing violations blocking a
        // PR that never touched them and concludes the tool is broken, rather than that
        // the checkout was.
        if (result.Gate is { DegradedReason: { } reason })
        {
            return $"⚠️ **Could not scope this gate to the PR's own changes** — {reason} " +
                   "Falling back to gating on the whole scan, so pre-existing violations count here.";
        }

        if (result.Gate is not { Scope: "diff" } gate)
        {
            return null;
        }

        if (gate.Blocked)
        {
            return $"🚫 **Blocking this PR** — {gate.BlockingErrorCount} " +
                   $"{Plural(gate.BlockingErrorCount, "error")} within this PR's own changed lines.";
        }

        if (result.Summary.ErrorCount > 0)
        {
            var verb = result.Summary.ErrorCount == 1 ? "is" : "are";
            return $"ℹ️ Not blocking this PR — the {result.Summary.ErrorCount} " +
                   $"{Plural(result.Summary.ErrorCount, "error")} above {verb} outside this PR's own changed lines.";
        }

        return null;
    }

    // A location-less diagnostic (ConventionCoverageAnalyzer reports against the whole
    // compilation) carries an empty FilePath and line 1, which rendered as "`:1`" in the
    // New/Resolved lists — a broken-looking link to a file that does not exist. The
    // grouped file sections below handle the same case with their own heading; this is
    // the shared renderer for the two flat lists.
    private static string LocationOf(Violation violation) =>
        string.IsNullOrEmpty(violation.FilePath)
            ? "repository-wide"
            : $"`{violation.FilePath}:{violation.Line}`";

    private static IEnumerable<Violation> Sorted(IEnumerable<Violation> violations) =>
        violations.OrderBy(v => v.FilePath, StringComparer.Ordinal).ThenBy(v => v.Line);

    private static string Plural(int count, string singular) => count == 1 ? singular : singular + "s";

    private static string SeverityIcon(string severity) => severity switch
    {
        "error" => "❌",
        "warning" => "⚠️",
        "info" => "ℹ️",
        _ => "•",
    };
}
