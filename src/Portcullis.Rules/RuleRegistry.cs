using System.Collections.Immutable;
using Microsoft.CodeAnalysis.Diagnostics;

namespace Portcullis.Rules;

/// <summary>
/// The single place rules are registered. A rule is a plain Roslyn
/// <see cref="DiagnosticAnalyzer"/>; its <c>DiagnosticDescriptor.Id</c> is the
/// portcullis rule id and its Category carries the architecture-standards principle id
/// ("P9") it implements — see docs/SPEC.md section 1 for the full rule-contract
/// mapping.
///
/// Naming deviates from the convention SPEC.md section 1 first wrote down, for a reason
/// recorded in full in docs/MUTATIONS.md: that convention was hyphenated
/// ("PORTCULLIS-P&lt;n&gt;-&lt;slug&gt;"), but <c>Microsoft.CodeAnalysis.Diagnostics</c>
/// rejects a hyphenated <c>Diagnostic.Id</c> at the exact
/// <c>CompilationAnalysisContext.ReportDiagnostic</c>/<c>SyntaxNodeAnalysisContext
/// .ReportDiagnostic</c> call sites this project's rules must use (throws
/// <see cref="System.ArgumentException"/>, "which is not a valid identifier" — verified
/// directly against Microsoft.CodeAnalysis.CSharp 4.11.0, not assumed). Rule ids here
/// use underscores instead: "PORTCULLIS_P&lt;n&gt;_&lt;SLUG&gt;", e.g.
/// "PORTCULLIS_P9_CONTROLLER_NO_DBCONTEXT" — the form SPEC.md now gives as well.
///
/// Track A (M2A/M3A) shipped the first three — <see cref="KernelBoundaryAnalyzer"/> (P2),
/// <see cref="ProgramManifestLayeringAnalyzer"/> (P9),
/// <see cref="ExtensibilityInheritanceAnalyzer"/> (P10) — deliberately not all six
/// principles docs/SPEC.md section 5 rates "Expressible" (see that section's own closing
/// note: "Track A's first 2–3 rules should come from the Expressible column"). A later
/// pass added the remaining three: <see cref="PersistencePortabilityAnalyzer"/> (P4),
/// <see cref="AntiCorruptionEdgeAnalyzer"/> (P11), <see cref="ObservabilityBuildTimeAnalyzer"/>
/// (P15) — bringing all 6 Expressible principles to parity. Results in
/// docs/MUTATIONS.md's follow-up section.
///
/// <see cref="ConventionCoverageAnalyzer"/> is not a principle rule. It reports when the
/// folder and file-name conventions the others match against find nothing, which is why
/// its Category is "Meta" rather than a "P&lt;n&gt;" id — it describes the gate's own
/// coverage, not a violation of any architecture-standards principle. It lives in the
/// registry rather than beside it because it must observe exactly the compilation the real
/// rules observe, through exactly the same <see cref="PortcullisConventions"/>; computing
/// coverage anywhere else would let it drift from what the rules actually did and
/// confidently report the wrong answer.
///
/// Nor are the four migration rules — <see cref="SystemWebUsageAnalyzer"/>,
/// <see cref="HttpContextCurrentAnalyzer"/>, <see cref="SyncOverAsyncAnalyzer"/> and
/// <see cref="ConfigurationManagerAnalyzer"/>, ids <c>PORTCULLIS_MIG_*</c>. They look for
/// .NET Framework idioms that survive a migration to modern .NET, which no single P1–P15
/// principle names, so their Category is "Migration" and their reasoning lives in
/// docs/rules/MIGRATION.md. Unlike the principle rules they are semantic rather than
/// convention-driven: each decision is made on a symbol the compiler bound (see
/// <see cref="MigrationSymbols"/>), never on a folder name or on the text of the source.
/// </summary>
public static class RuleRegistry
{
    public static ImmutableArray<DiagnosticAnalyzer> All { get; } = ImmutableArray.Create<DiagnosticAnalyzer>(
        new KernelBoundaryAnalyzer(),
        new ProgramManifestLayeringAnalyzer(),
        new ExtensibilityInheritanceAnalyzer(),
        new PersistencePortabilityAnalyzer(),
        new AntiCorruptionEdgeAnalyzer(),
        new ObservabilityBuildTimeAnalyzer(),
        new SystemWebUsageAnalyzer(),
        new HttpContextCurrentAnalyzer(),
        new SyncOverAsyncAnalyzer(),
        new ConfigurationManagerAnalyzer(),
        new ConventionCoverageAnalyzer());
}
