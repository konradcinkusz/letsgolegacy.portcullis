using System.Text.Encodings.Web;
using System.Text.Json;
using Portcullis.Engine.Model;

namespace Portcullis.Engine.Findings;

/// <summary>
/// Thrown when a baseline file cannot be used. The message is written for the person running
/// the scan: it names the file and says what to do about it.
/// </summary>
public sealed class BaselineFileException(string message) : Exception(message);

/// <summary>
/// Reads and writes the baseline file: the fingerprints of the findings a team accepted as
/// pre-existing, as JSON a reviewer can read in a pull request diff (docs/SARIF.md).
///
/// Every finding is written with its rule, file, line and message beside the fingerprint.
/// Only the fingerprint is ever matched; the rest is there so that a pull request which grows
/// the baseline shows a reviewer exactly what is being accepted, instead of a column of hashes.
/// The line is where the finding was when the file was written and is not updated after.
///
/// A file that cannot be used is an error, never an empty baseline. Reading a missing or
/// malformed baseline as "accepts nothing" would be safe for the gate — stricter, not more
/// lenient — but it would also silently turn a typo in a workflow into a gate that blocks on
/// every pre-existing finding, and the person looking at the red build would have nothing
/// telling them why.
/// </summary>
public static class BaselineFile
{
    /// <summary>The file format's version. A reader accepts any 1.x file.</summary>
    public const string SchemaVersion = "1.0.0";

    private const string About =
        "Findings accepted as pre-existing. Portcullis still reports them, but they do not block " +
        "and are left out of the filtered SARIF. Each is matched by its fingerprint (rule, file " +
        "and line text), so the line numbers are where each finding was when this file was " +
        "written. Regenerate with: portcullis scan <path> --baseline <this file> --write-baseline. " +
        "See docs/SARIF.md.";

    private static readonly JsonSerializerOptions ReadOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    // "\n" whatever the platform, so a baseline written on Windows and one written on Linux
    // are the same bytes and a regenerated file diffs only where findings changed. Relaxed
    // escaping keeps the messages' quotes and angle brackets readable in review.
    private static readonly JsonSerializerOptions WriteOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true,
        NewLine = "\n",
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    /// <summary>Reads a baseline file, or throws <see cref="BaselineFileException"/> saying why it cannot be used.</summary>
    public static Baseline Read(string path)
    {
        string json;
        try
        {
            json = File.ReadAllText(path);
        }
        catch (Exception ex) when (ex is FileNotFoundException or DirectoryNotFoundException)
        {
            throw new BaselineFileException(
                $"the baseline file '{path}' does not exist. Write one with --write-baseline, or leave out --baseline.");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            throw new BaselineFileException($"could not read the baseline file '{path}': {ex.Message}");
        }

        BaselineDocument? document;
        try
        {
            document = JsonSerializer.Deserialize<BaselineDocument>(json, ReadOptions);
        }
        catch (JsonException ex)
        {
            throw new BaselineFileException($"the baseline file '{path}' is not valid JSON: {ex.Message}");
        }

        if (document is null)
        {
            throw new BaselineFileException($"the baseline file '{path}' holds no baseline (it is JSON null).");
        }

        if (document.SchemaVersion?.Split('.')[0] != "1")
        {
            throw new BaselineFileException(
                $"the baseline file '{path}' has schemaVersion '{document.SchemaVersion}', and this version " +
                $"of Portcullis reads {SchemaVersion}. Regenerate it with --write-baseline.");
        }

        if (document.FingerprintVersion != FindingFingerprint.Version)
        {
            throw new BaselineFileException(
                $"the baseline file '{path}' holds '{document.FingerprintVersion}' fingerprints, and this version " +
                $"of Portcullis computes '{FindingFingerprint.Version}', so none of them could match. " +
                "Regenerate it with --write-baseline.");
        }

        if (document.Findings is null)
        {
            throw new BaselineFileException($"the baseline file '{path}' has no \"findings\" array.");
        }

        var fingerprints = new List<string>(document.Findings.Count);
        for (var i = 0; i < document.Findings.Count; i++)
        {
            if (document.Findings[i]?.Fingerprint is not { Length: > 0 } fingerprint)
            {
                throw new BaselineFileException(
                    $"the baseline file '{path}' has a finding with no fingerprint (findings[{i}]).");
            }

            fingerprints.Add(fingerprint);
        }

        return Baseline.Of(fingerprints, path);
    }

    /// <summary>
    /// Writes every given finding into a baseline file, replacing whatever was there, and
    /// returns how many were written. Entries are sorted by file, line and rule, so rewriting a
    /// baseline after a change produces a diff of exactly the findings that changed.
    /// </summary>
    public static int Write(string path, IEnumerable<Violation> violations)
    {
        var entries = new List<BaselineEntry>();
        foreach (var violation in violations)
        {
            if (violation.Fingerprint is null)
            {
                // Only a fresh scan's violations carry fingerprints. One without could never be
                // matched when the file is read back, so writing it would record an acceptance
                // that does not work.
                throw new ArgumentException(
                    $"'{violation.RuleId}' at {violation.FilePath}:{violation.Line} has no fingerprint; " +
                    "only violations from a fresh scan can be written to a baseline.",
                    nameof(violations));
            }

            entries.Add(new BaselineEntry(
                violation.Fingerprint, violation.RuleId, violation.FilePath, violation.Line, violation.Message));
        }

        var sorted = entries
            .OrderBy(e => e.FilePath, StringComparer.Ordinal)
            .ThenBy(e => e.Line)
            .ThenBy(e => e.RuleId, StringComparer.Ordinal)
            .ThenBy(e => e.Fingerprint, StringComparer.Ordinal)
            .ToList();

        var directory = Path.GetDirectoryName(Path.GetFullPath(path));
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        var document = new BaselineDocument(SchemaVersion, FindingFingerprint.Version, About, [.. sorted]);
        File.WriteAllText(path, JsonSerializer.Serialize(document, WriteOptions) + "\n");
        return sorted.Count;
    }

    private sealed record BaselineDocument(
        string? SchemaVersion,
        string? FingerprintVersion,
        string? About,
        List<BaselineEntry?>? Findings);

    private sealed record BaselineEntry(
        string? Fingerprint,
        string? RuleId,
        string? FilePath,
        int Line,
        string? Message);
}
