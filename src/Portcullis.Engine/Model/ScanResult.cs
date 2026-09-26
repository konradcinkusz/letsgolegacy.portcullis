namespace Portcullis.Engine.Model;

/// <summary>
/// The exact shape `portcullis scan` produces on stdout, as frozen in docs/SPEC.md
/// section 3. This is the contract Track B mocks before Track A's rules exist.
///
/// <c>Gate</c> is a later, additive addition (post-M0, the diff-scoped-gate milestone —
/// see docs/SPEC.md section 3's own addendum) — nullable and defaulted so every existing
/// positional construction and every pre-existing JSON fixture predating this field keeps
/// working unchanged. A fresh `portcullis scan` always populates it now; only foreign or
/// historical JSON lacking the field deserializes it as null.
/// </summary>
public sealed record ScanResult(
    string SchemaVersion,
    string EngineVersion,
    string ScannedPath,
    DateTime ScanStartedUtc,
    double ScanDurationMs,
    int FilesScanned,
    int RulesEvaluated,
    IReadOnlyList<Violation> Violations,
    ScanSummary Summary,
    GateResult? Gate = null,
    ScanConfiguration? Configuration = null);

/// <summary>
/// Which configuration file the scan actually used, if any — additive and defaulted for
/// the same reason <c>Gate</c> is.
///
/// Reported because "portcullis.json was not where you thought it was" and "portcullis.json
/// said nothing about this" are indistinguishable from the outside, and both look exactly
/// like a passing scan. <c>Path</c> null means no config file was found and the built-in
/// defaults are in force, which is a legitimate and common state, not an error.
/// </summary>
/// <param name="Path">The config file read, or null when none was found.</param>
/// <param name="Error">Set when a config file exists but could not be parsed; the scan ran on defaults.</param>
public sealed record ScanConfiguration(string? Path, string? Error);

public sealed record ScanSummary(int ErrorCount, int WarningCount, int InfoCount)
{
    public static ScanSummary From(IReadOnlyList<Violation> violations) => new(
        violations.Count(v => v.Severity == "error"),
        violations.Count(v => v.Severity == "warning"),
        violations.Count(v => v.Severity == "info"));
}

/// <summary>
/// The actual merge-gate verdict, computed once here rather than recomputed
/// independently by each of <c>Portcullis.Cli</c> and <c>Portcullis.CiComment</c> (which,
/// before this, each read <c>Summary.ErrorCount</c> directly and could in principle have
/// drifted apart). <c>Scope</c> is <c>"all"</c> — today's original, unconditional
/// behavior, `errorCount > 0` over the whole scan — when no diff range was supplied to
/// the scan, or <c>"diff"</c> when one was: then <c>Blocked</c>/<c>BlockingErrorCount</c>
/// only count error-severity violations whose location falls inside that range, so a
/// pre-existing error elsewhere in the tree no longer fails a PR that never touches it.
/// </summary>
/// <param name="DegradedReason">
/// Set only when a diff-scoped gate was requested but could not be computed, and the scan
/// fell back to <c>Scope "all"</c> rather than scoping to a range set the provider could
/// not produce. Null on every ordinary scan, in both scopes.
///
/// This is the observable half of the fail-open fix: without it, a caller seeing
/// <c>scope: "all"</c> cannot tell "no range was requested" from "a range was requested
/// and git broke", and the second case would look like a deliberate whole-tree scan.
/// Trailing and defaulted, so it adds one JSON key and breaks no existing construction or
/// fixture — see this file's own note on <c>Gate</c> for the same reasoning.
/// </param>
public sealed record GateResult(
    bool Blocked,
    string Scope,
    int BlockingErrorCount,
    string? DegradedReason = null);
