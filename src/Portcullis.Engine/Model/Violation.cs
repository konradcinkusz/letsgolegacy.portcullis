namespace Portcullis.Engine.Model;

/// <summary>
/// One architecture-rule violation, as frozen in docs/SPEC.md section 2.
/// Field names and casing here are the JSON contract Track B mocks against.
/// </summary>
/// <param name="Fingerprint">
/// The finding's identity across commits (<see cref="Findings.FindingFingerprint"/>): the
/// rule, the file and the code on the line — not the line number, so it survives edits
/// elsewhere in the file. It is what a baseline file stores and what SARIF carries as a
/// partial fingerprint. Every fresh scan sets it; only JSON written before it existed reads
/// back null. Added with the SARIF and baseline work (ticket R3, docs/SARIF.md), trailing and
/// defaulted for the same reason <c>ScanResult.Gate</c> is: no existing construction or
/// fixture changes.
/// </param>
/// <param name="Baselined">
/// True when the baseline the scan was given accepts this finding: it is still reported, but
/// it does not count toward the gate. Always false when no baseline was given.
/// </param>
public sealed record Violation(
    string RuleId,
    string FilePath,
    int Line,
    string Message,
    string Severity,
    string? Fingerprint = null,
    bool Baselined = false);
