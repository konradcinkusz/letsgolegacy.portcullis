namespace Portcullis.Engine.Model;

/// <summary>
/// One architecture-rule violation, as frozen in docs/SPEC.md section 2.
/// Field names and casing here are the JSON contract Track B mocks against.
/// </summary>
public sealed record Violation(
    string RuleId,
    string FilePath,
    int Line,
    string Message,
    string Severity);
