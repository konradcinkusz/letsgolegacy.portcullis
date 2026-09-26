using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using Portcullis.Engine.Model;

namespace Portcullis.Engine.Findings;

/// <summary>
/// A finding's identity across commits, which a baseline file stores and SARIF carries as a
/// partial fingerprint (docs/SARIF.md).
///
/// The inputs are the rule, the file, and the text of the line the finding is on with its
/// whitespace normalised — deliberately not the line number. A baseline keyed on line numbers
/// goes stale at the first edit above a finding: every accepted finding below it moves, stops
/// matching, and comes back as "new", so the baseline blocks exactly the pull requests it was
/// written to let through. Keyed on the code instead, a finding keeps its identity while lines
/// are added or removed around it and while it is re-indented, and loses it when the line it
/// sits on is itself rewritten — which is when a reviewer should look at it again anyway.
///
/// Two findings of the same rule on identical text in the same file (two copies of one line,
/// say) are told apart by occurrence: the first, in file order, is occurrence 0. If a third
/// copy is added above the other two, the count of new findings is still right (one); which
/// of the three identical lines it is reported on is not knowable from text alone.
///
/// A finding with no source location (a repository-wide one) has no line to read, so its
/// message stands in for the line text: for those rules the message is what distinguishes one
/// finding from another.
///
/// The message itself is not an input for located findings. Rule messages are prose and get
/// reworded between versions; a fingerprint that changed whenever they did would turn a tool
/// upgrade into a baseline rewrite. Severity is not an input either: provenance escalation and
/// configuration change it without changing what was found.
/// </summary>
public static class FindingFingerprint
{
    /// <summary>
    /// The scheme's name: written into every baseline file, and the key under SARIF's
    /// <c>partialFingerprints</c>. A change to what is hashed is a new version, never an edit
    /// to this one, because every baseline on disk was written against it.
    /// </summary>
    public const string Version = "portcullisFingerprint/v1";

    /// <summary>
    /// The fingerprint of one finding: lowercase hexadecimal SHA-256 of the scheme version,
    /// the rule id, the file path, the normalised line text and the occurrence, one per line.
    /// </summary>
    /// <param name="ruleId">The rule that fired.</param>
    /// <param name="filePath">The violation's file path, as reported: relative to the scan root, with '/' separators.</param>
    /// <param name="lineText">The text of the line the finding is on, or the message for a finding with no location.</param>
    /// <param name="occurrence">0 for the first finding with these other inputs in file order, 1 for the second, and so on.</param>
    public static string Compute(string ruleId, string filePath, string lineText, int occurrence)
    {
        var canonical = string.Join(
            "\n",
            Version,
            ruleId,
            filePath,
            Normalize(lineText),
            occurrence.ToString(CultureInfo.InvariantCulture));
        return Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(canonical)));
    }

    /// <summary>
    /// Trims the text and collapses every run of whitespace inside it to one space, so that
    /// re-indenting a line, or re-wrapping it with tabs instead of spaces, leaves its
    /// fingerprint alone.
    /// </summary>
    public static string Normalize(string text) =>
        string.Join(' ', text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));

    /// <summary>
    /// Fingerprints a scan's findings, which must arrive in their final report order: the
    /// occurrence counter runs in that order, so a different order would number identical
    /// findings differently.
    /// </summary>
    internal static List<Violation> Assign(IEnumerable<(Violation Violation, string LineText)> ordered)
    {
        var occurrences = new Dictionary<(string RuleId, string FilePath, string LineText), int>();
        var fingerprinted = new List<Violation>();

        foreach (var (violation, lineText) in ordered)
        {
            var normalized = Normalize(lineText);
            var key = (violation.RuleId, violation.FilePath, normalized);
            occurrences.TryGetValue(key, out var occurrence);
            occurrences[key] = occurrence + 1;

            fingerprinted.Add(violation with
            {
                Fingerprint = Compute(violation.RuleId, violation.FilePath, normalized, occurrence),
            });
        }

        return fingerprinted;
    }
}
