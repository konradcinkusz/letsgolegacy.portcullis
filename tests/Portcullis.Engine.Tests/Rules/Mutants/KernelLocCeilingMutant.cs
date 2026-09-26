using System.Collections.Immutable;
using Portcullis.Rules;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Diagnostics;

namespace Portcullis.Engine.Tests.Rules.Mutants;

/// <summary>
/// Mutation-pass variant of <see cref="KernelBoundaryAnalyzer"/>'s LOC-ceiling check
/// (docs/MUTATIONS.md, variant "kernel-loc-ceiling-folder-typo"). Only the folder-name
/// convention list is changed, from "ServiceDefaults" to the singular "ServiceDefault" —
/// a one-character typo that silently stops the kernel-folder classification from ever
/// matching a real "*.ServiceDefaults" project. Reuses the real
/// <see cref="KernelBoundaryAnalyzer.LocCeilingRule"/> descriptor so its output is
/// identical in shape to the real rule; only whether it ever fires differs.
/// </summary>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class KernelLocCeilingMutant : DiagnosticAnalyzer
{
    private static readonly string[] KernelFolderSegments = ["ServiceDefault", "Kernel", "SharedKernel"];

    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics { get; } =
        ImmutableArray.Create(KernelBoundaryAnalyzer.LocCeilingRule);

    public override void Initialize(AnalysisContext context)
    {
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.EnableConcurrentExecution();
        context.RegisterCompilationAction(ctx =>
        {
            var kernelTrees = ctx.Compilation.SyntaxTrees
                .Where(t => HasSegment(t.FilePath))
                .ToList();
            if (kernelTrees.Count == 0)
            {
                return;
            }

            var counts = kernelTrees.Select(t => (Tree: t, Lines: t.GetText().Lines.Count)).ToList();
            var total = counts.Sum(c => c.Lines);
            if (total <= KernelBoundaryAnalyzer.LocCeiling)
            {
                return;
            }

            var largest = counts.OrderByDescending(c => c.Lines).First();
            ctx.ReportDiagnostic(Diagnostic.Create(
                KernelBoundaryAnalyzer.LocCeilingRule,
                Location.Create(largest.Tree, largest.Tree.GetText().Lines[0].Span),
                "kernel",
                total,
                KernelBoundaryAnalyzer.LocCeiling,
                largest.Tree.FilePath,
                largest.Lines));
        });
    }

    private static bool HasSegment(string? filePath)
    {
        if (string.IsNullOrEmpty(filePath))
        {
            return false;
        }

        var parts = filePath.Split('/', '\\', StringSplitOptions.RemoveEmptyEntries);
        return parts.Any(p => KernelFolderSegments.Any(seg =>
            p.Equals(seg, StringComparison.OrdinalIgnoreCase) || p.EndsWith("." + seg, StringComparison.OrdinalIgnoreCase)));
    }
}
