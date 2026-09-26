using System.Collections.Immutable;
using Portcullis.Rules;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;

namespace Portcullis.Engine.Tests.Rules.Mutants;

/// <summary>
/// Mutation-pass variant of <see cref="ConventionCoverageAnalyzer"/>'s aggregate check.
///
/// The real rule reports only when NO convention matched anything — that all-or-nothing
/// condition is what keeps it quiet on ordinary codebases that legitimately lack some of
/// these shapes. This mutant keeps the same descriptor and the same "did anything match"
/// question but inverts how it is combined: it reports as soon as any single convention
/// misses, which is the per-convention behaviour an earlier cut of the rule had and which
/// produced four warnings on healthy code.
///
/// So the mutation is not "the rule stops firing" but "the rule fires when it should be
/// silent" — which is the failure mode that actually matters for a meta-rule, since a
/// noisy one gets switched off and then reports nothing when it counts. The paired test
/// therefore asserts the real rule is SILENT on a codebase where one convention matches,
/// and the mutant is not.
/// </summary>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class ConventionCoverageAggregateMutant : DiagnosticAnalyzer
{
    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics { get; } =
        ImmutableArray.Create(ConventionCoverageAnalyzer.NoConventionMatchedRule);

    private static readonly string[] KernelFolders = ["ServiceDefaults", "Kernel", "SharedKernel"];
    private static readonly string[] EntityFolders = ["Domain", "Entities"];
    private static readonly string[] AdapterFolders = ["Infrastructure", "Adapters", "Integrations"];
    private static readonly char[] PathSeparators = ['/', '\\'];

    public override void Initialize(AnalysisContext context)
    {
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.EnableConcurrentExecution();
        context.RegisterCompilationAction(ctx =>
        {
            var paths = ctx.Compilation.SyntaxTrees
                .Select(t => t.FilePath)
                .Where(p => !string.IsNullOrEmpty(p))
                .ToList();

            if (paths.Count == 0)
            {
                return;
            }

            foreach (var group in new[] { KernelFolders, EntityFolders, AdapterFolders })
            {
                // The mutation: reports per unmatched convention instead of only when
                // every one of them missed.
                if (!paths.Any(p => HasSegment(p, group)))
                {
                    ctx.ReportDiagnostic(Diagnostic.Create(
                        ConventionCoverageAnalyzer.NoConventionMatchedRule,
                        Location.None,
                        string.Join(", ", group),
                        "portcullis.json"));
                }
            }
        });
    }

    private static bool HasSegment(string filePath, string[] segments) =>
        filePath.Split(PathSeparators, StringSplitOptions.RemoveEmptyEntries)
            .Any(p => segments.Any(seg =>
                p.Equals(seg, StringComparison.OrdinalIgnoreCase)
                || p.EndsWith("." + seg, StringComparison.OrdinalIgnoreCase)));
}
