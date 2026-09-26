namespace Portcullis.Engine.Findings;

/// <summary>
/// The findings a team has accepted as pre-existing, by <see cref="FindingFingerprint"/>. A
/// finding the baseline accepts is still reported — in the scan result, in the comment — but
/// it does not count toward the gate and it is left out of the filtered SARIF, so only what
/// is new can block (docs/SARIF.md).
///
/// Data, handed to <see cref="Scanner.ScanAsync"/> by its caller the same way a provenance
/// report is: the engine does not go looking for a baseline file, so a scan without one
/// behaves exactly as it always has. <see cref="BaselineFile"/> reads and writes the file.
/// </summary>
public sealed class Baseline
{
    // Null means "accepts every finding", which is what --write-baseline gates against.
    private readonly HashSet<string>? _fingerprints;

    private Baseline(HashSet<string>? fingerprints, string? path)
    {
        _fingerprints = fingerprints;
        Path = path;
    }

    /// <summary>Where the baseline was read from or is about to be written to; null for one built in memory.</summary>
    public string? Path { get; }

    /// <summary>
    /// True for the baseline <c>--write-baseline</c> gates against: it accepts every finding
    /// of the scan, because the scan's findings are what is about to be written into it.
    /// </summary>
    public bool AcceptsEverything => _fingerprints is null;

    /// <summary>How many fingerprints this baseline holds; null when it <see cref="AcceptsEverything"/>.</summary>
    public int? Count => _fingerprints?.Count;

    /// <summary>A baseline accepting exactly these fingerprints.</summary>
    public static Baseline Of(IEnumerable<string> fingerprints, string? path = null) =>
        new(new HashSet<string>(fingerprints, StringComparer.Ordinal), path);

    /// <summary>
    /// A baseline accepting whatever the scan finds — the verdict a freshly written baseline
    /// gives the scan that wrote it. See <see cref="AcceptsEverything"/>.
    /// </summary>
    public static Baseline Everything(string? path = null) => new(null, path);

    /// <summary>Whether a finding with this fingerprint is accepted.</summary>
    public bool Accepts(string? fingerprint) =>
        _fingerprints is null || (fingerprint is not null && _fingerprints.Contains(fingerprint));
}
