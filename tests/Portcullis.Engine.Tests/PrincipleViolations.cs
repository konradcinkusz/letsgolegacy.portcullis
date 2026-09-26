using Portcullis.Engine.Model;

namespace Portcullis.Engine.Tests;

/// <summary>
/// Filters a scan down to violations of an architecture-standards principle, dropping the
/// <c>Meta</c>-category coverage diagnostics <see cref="Portcullis.Rules.ConventionCoverageAnalyzer"/>
/// reports.
///
/// Needed because these tests scan throwaway directories holding one or two files, which
/// legitimately match none of the folder conventions — so a real, correct
/// PORTCULLIS_NO_CONVENTION_MATCHED accompanies every one of them. Filtering by rule id
/// rather than counting everything also states what each test is actually about: they
/// exercise one principle rule, not the total diagnostic count, and were only ever
/// asserting on the total because nothing else could fire.
///
/// Note this is a deliberately *narrow* filter — it removes only the meta category, so a
/// test can never accidentally hide a principle rule that started or stopped firing.
/// </summary>
internal static class PrincipleViolations
{
    public static IReadOnlyList<Violation> OfPrinciples(this ScanResult result) =>
        result.Violations.Where(v => !IsMeta(v.RuleId)).ToList();

    public static int PrincipleWarningCount(this ScanResult result) =>
        result.OfPrinciples().Count(v => v.Severity == "warning");

    private static bool IsMeta(string ruleId) =>
        ruleId is "PORTCULLIS_CONVENTION_UNMATCHED" or "PORTCULLIS_NO_CONVENTION_MATCHED";
}
