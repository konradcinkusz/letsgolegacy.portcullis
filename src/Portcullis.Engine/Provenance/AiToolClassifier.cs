using System.Text.RegularExpressions;

namespace Portcullis.Engine.Provenance;

/// <summary>
/// The classification policy: given one commit's author identity and trailers, decide
/// its <see cref="ProvenanceSource"/> and <see cref="ProvenanceAttribution"/>. Pure and
/// git-agnostic — <see cref="GitProvenanceProvider"/> owns talking to git and hands this
/// class already-parsed strings.
///
/// Tool signatures below are the real, verified conventions each tool actually emits
/// (not assumed): Claude Code's `Co-Authored-By: &lt;model&gt; &lt;noreply@anthropic.com&gt;`
/// trailer (and its `Claude-Session:` trailer); GitHub Copilot's VS Code-added
/// `Co-authored-by: Copilot &lt;copilot@github.com&gt;` trailer (gated by the
/// `git.addAICoAuthor` setting) and, separately, the Copilot coding agent's own bot
/// commit identity (`copilot-swe-agent[bot]`, `*-Copilot@users.noreply.github.com`);
/// Cursor's `Co-authored-by: Cursor &lt;cursoragent@cursor.com&gt;` (CLI/SDK agent) and
/// `Made-with: Cursor` (IDE agent) conventions. By design (docs/SPEC.md section 4),
/// authorship is never inferred from code style — only from these structural git
/// signals.
/// </summary>
internal static class AiToolClassifier
{
    private const string MethodCommitTrailer = "commit-trailer";
    private const string MethodGitBlameAuthorship = "git-blame-authorship";

    private const double ConfidenceAutonomousBotAuthor = 0.95;
    private const double ConfidenceAiCoAuthorTrailer = 0.85;
    private const double ConfidenceMixedHumanAndAi = 0.7;
    private const double ConfidenceHumanNoAiSignal = 0.7;
    private const double ConfidenceUnrecognizedBotIdentity = 0.5;

    private static readonly (string ToolId, Func<string, string, bool> MatchesIdentity)[] KnownToolIdentities =
    [
        ("claude-code", (name, email) =>
            EmailHasDomain(email, "anthropic.com") || NameIs(name, "claude")),

        ("github-copilot", (name, email) =>
            EmailIs(email, "copilot@github.com") ||
            NameIs(name, "copilot") ||
            NameContains(name, "copilot-swe-agent") ||
            EmailContains(email, "copilot@users.noreply.github.com")),

        ("cursor", (name, email) =>
            EmailHasDomain(email, "cursor.com") ||
            NameIs(name, "cursor") ||
            NameContains(name, "cursor agent")),
    ];

    private static readonly Regex NameEmailPattern = new(@"^(?<name>.*?)\s*<(?<email>[^>]*)>\s*$", RegexOptions.Compiled);

    public static AiToolClassification Classify(
        string authorName, string authorEmail, IReadOnlyList<(string Key, string Value)> trailers)
    {
        string? matchedTool = null;
        var matchedViaTrailer = false;
        var humanCoAuthorCount = 0;

        foreach (var (key, value) in trailers)
        {
            if (!string.Equals(key, "co-authored-by", StringComparison.OrdinalIgnoreCase))
                continue;

            var (name, email) = ParseNameEmail(value);
            if (TryMatchIdentity(name, email, out var tool))
            {
                if (matchedTool is null)
                {
                    matchedTool = tool;
                    matchedViaTrailer = true;
                }
            }
            else
            {
                humanCoAuthorCount++;
            }
        }

        if (matchedTool is null)
        {
            foreach (var (key, value) in trailers)
            {
                if (TryMatchNamedTrailer(key, value, out var tool))
                {
                    matchedTool = tool;
                    matchedViaTrailer = true;
                    break;
                }
            }
        }

        if (matchedTool is null && TryMatchIdentity(authorName, authorEmail, out var authorTool))
        {
            matchedTool = authorTool;
            matchedViaTrailer = false;
        }

        if (matchedTool is not null)
        {
            if (humanCoAuthorCount > 0)
                return new AiToolClassification(ProvenanceSource.Mixed, ConfidenceMixedHumanAndAi, matchedTool, MethodCommitTrailer);

            return matchedViaTrailer
                ? new AiToolClassification(ProvenanceSource.Ai, ConfidenceAiCoAuthorTrailer, matchedTool, MethodCommitTrailer)
                : new AiToolClassification(ProvenanceSource.Ai, ConfidenceAutonomousBotAuthor, matchedTool, MethodGitBlameAuthorship);
        }

        if (IsBotShapedIdentity(authorName))
            return new AiToolClassification(ProvenanceSource.Unknown, ConfidenceUnrecognizedBotIdentity, null, MethodGitBlameAuthorship);

        return new AiToolClassification(ProvenanceSource.Human, ConfidenceHumanNoAiSignal, null, MethodGitBlameAuthorship);
    }

    private static bool TryMatchIdentity(string name, string email, out string toolId)
    {
        foreach (var (id, matches) in KnownToolIdentities)
        {
            if (matches(name, email))
            {
                toolId = id;
                return true;
            }
        }

        toolId = "";
        return false;
    }

    private static bool TryMatchNamedTrailer(string key, string value, out string toolId)
    {
        switch (key.Trim().ToLowerInvariant())
        {
            case "made-with" when value.Contains("cursor", StringComparison.OrdinalIgnoreCase):
                toolId = "cursor";
                return true;
            case "claude-session":
                toolId = "claude-code";
                return true;
            default:
                toolId = "";
                return false;
        }
    }

    private static bool IsBotShapedIdentity(string name) =>
        name.EndsWith("[bot]", StringComparison.OrdinalIgnoreCase);

    private static (string Name, string Email) ParseNameEmail(string value)
    {
        var match = NameEmailPattern.Match(value);
        return match.Success
            ? (match.Groups["name"].Value.Trim(), match.Groups["email"].Value.Trim())
            : (value.Trim(), "");
    }

    private static bool EmailHasDomain(string email, string domain) =>
        email.EndsWith("@" + domain, StringComparison.OrdinalIgnoreCase);

    private static bool EmailIs(string email, string expected) =>
        string.Equals(email, expected, StringComparison.OrdinalIgnoreCase);

    private static bool EmailContains(string email, string fragment) =>
        email.Contains(fragment, StringComparison.OrdinalIgnoreCase);

    private static bool NameIs(string name, string expected) =>
        string.Equals(name, expected, StringComparison.OrdinalIgnoreCase);

    private static bool NameContains(string name, string fragment) =>
        name.Contains(fragment, StringComparison.OrdinalIgnoreCase);
}

internal readonly record struct AiToolClassification(
    ProvenanceSource Source, double Confidence, string? Tool, string Method);
