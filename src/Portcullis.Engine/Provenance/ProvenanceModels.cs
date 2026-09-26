namespace Portcullis.Engine.Provenance;

/// <summary>
/// The provenance signal contract frozen in docs/SPEC.md section 4: the shape of
/// "this range of lines is AI-authored" as an input the rule engine can consume.
/// Types only — no implementation lives here. Track C (track-c-provenance) implements
/// <see cref="IProvenanceProvider"/>; wiring a provider into a scan is integration
/// work (milestone M4), not part of this skeleton.
///
/// Deliberately NOT shaped like copilot-scope's edit-survival signal: that one is a
/// session-level average computed by the editor (Value/Count over a batch of edits,
/// see copilot-scope's Domain/SessionStore.cs), with no file, line, or commit identity
/// attached to any sample. Every range here carries a file path, a 1-based line span,
/// and a commit — line-level attribution, not a session scalar.
/// </summary>
public enum ProvenanceSource
{
    Human,
    Ai,
    Mixed,
    Unknown,
}

public sealed record ProvenanceAttribution(
    string? Tool,
    string Method,
    string? CommitSha);

public sealed record ProvenanceRange(
    string FilePath,
    int StartLine,
    int EndLine,
    ProvenanceSource Source,
    double Confidence,
    ProvenanceAttribution Attribution);

/// <summary>
/// Whether a <see cref="ProvenanceReport"/>'s <c>Ranges</c> are a trustworthy answer to
/// "which lines did this range change", or an answer the provider could not actually
/// produce.
///
/// This distinction exists because an empty <c>Ranges</c> list is otherwise ambiguous in
/// exactly the way that matters most: "this diff genuinely changed nothing" and "git
/// failed, so I don't know what it changed" produced the identical value, and a
/// diff-scoped gate reads both as "nothing to block on". That made
/// <see cref="IProvenanceProvider"/> fail *open* — a corrupted checkout, a force-pushed
/// base branch, a <c>provenanceRoot</c> that is not a git repository, or a missing git
/// binary all turned a blocking gate green while reporting success. Written up as a
/// known, flagged-not-fixed risk in docs/DIFF-GATE.md section 3 at the time; this enum is
/// the fix, and the reason that section is now a resolved note rather than a live one.
///
/// <see cref="Degraded"/> is deliberately about the *range set* only, not about
/// attribution. A blame or classification failure still yields a real, correctly-bounded
/// range (reported as <see cref="ProvenanceSource.Unknown"/>) — the gate can still scope
/// to it honestly, so that is not degradation. Only a failure that leaves the provider
/// unable to say which lines changed sets this.
/// </summary>
public enum ProvenanceStatus
{
    /// <summary>The ranges are the provider's real, complete answer — including a real, empty one.</summary>
    Complete,

    /// <summary>The provider could not determine the changed lines. <c>Ranges</c> is not an answer.</summary>
    Degraded,
}

/// <param name="Status">
/// Whether <paramref name="Ranges"/> is a real answer. Trailing and defaulted so every
/// existing three-argument construction — the CLI, the tests, and any caller building a
/// report by hand — keeps compiling untouched. Safe as a positional-record arity change
/// specifically because Portcullis.Engine is not itself published as a NuGet package (see
/// docs/DISTRIBUTION.md: the three published ids are Portcullis.Analyzers, Portcullis.Cli and
/// Portcullis.CiComment), so no consumer has compiled against the old constructor.
/// </param>
/// <param name="DegradedReason">
/// Operator-facing explanation of what failed, surfaced verbatim by the CLI on stderr and
/// in <c>Gate.DegradedReason</c>. Null whenever <paramref name="Status"/> is
/// <see cref="ProvenanceStatus.Complete"/>.
/// </param>
public sealed record ProvenanceReport(
    string SchemaVersion,
    string Commit,
    IReadOnlyList<ProvenanceRange> Ranges,
    ProvenanceStatus Status = ProvenanceStatus.Complete,
    string? DegradedReason = null);

/// <summary>
/// Implemented by Track C. Given a commit or diff range, returns per-line
/// AI-authorship tags the rule engine can use to apply a stricter threshold.
/// </summary>
public interface IProvenanceProvider
{
    ProvenanceReport GetProvenance(string commitOrRange);
}
