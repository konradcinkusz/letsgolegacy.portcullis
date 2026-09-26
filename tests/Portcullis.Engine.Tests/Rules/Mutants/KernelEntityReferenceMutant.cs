using System.Collections.Immutable;
using Portcullis.Rules;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;

namespace Portcullis.Engine.Tests.Rules.Mutants;

/// <summary>
/// Mutation-pass variant of <see cref="KernelBoundaryAnalyzer"/>'s
/// kernel-must-not-reference-entities check (docs/MUTATIONS.md, variant
/// "kernel-entity-reference-folder-typo"). The entity-folder convention list is
/// changed from "Domain" to the plural "Domains" — real projects use the singular, so
/// this mutant's entity-folder check never matches, and no kernel-to-entity reference
/// is ever flagged. Reuses the real
/// <see cref="KernelBoundaryAnalyzer.KernelEntityReferenceRule"/> descriptor.
/// </summary>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class KernelEntityReferenceMutant : DiagnosticAnalyzer
{
    private static readonly string[] EntityFolderSegments = ["Domains", "Entities"];

    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics { get; } =
        ImmutableArray.Create(KernelBoundaryAnalyzer.KernelEntityReferenceRule);

    public override void Initialize(AnalysisContext context)
    {
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.EnableConcurrentExecution();
        context.RegisterCompilationAction(ctx =>
        {
            var compilation = ctx.Compilation;
            var kernelTrees = compilation.SyntaxTrees
                .Where(t => (t.FilePath ?? string.Empty).Contains("ServiceDefaults", StringComparison.OrdinalIgnoreCase))
                .ToList();

            foreach (var kernelTree in kernelTrees)
            {
                var semanticModel = compilation.GetSemanticModel(kernelTree);
                var root = kernelTree.GetRoot(ctx.CancellationToken);

                foreach (var name in root.DescendantNodes().OfType<SimpleNameSyntax>())
                {
                    var symbol = semanticModel.GetSymbolInfo(name, ctx.CancellationToken).Symbol;
                    var typeSymbol = symbol switch
                    {
                        INamedTypeSymbol t => t,
                        IMethodSymbol { MethodKind: MethodKind.Constructor } m => m.ContainingType,
                        _ => null,
                    };

                    if (typeSymbol is null)
                    {
                        continue;
                    }

                    var entityLocation = typeSymbol.Locations.FirstOrDefault(l =>
                        l.IsInSource && HasSegment(l.SourceTree?.FilePath));
                    if (entityLocation is null)
                    {
                        continue;
                    }

                    ctx.ReportDiagnostic(Diagnostic.Create(
                        KernelBoundaryAnalyzer.KernelEntityReferenceRule,
                        name.GetLocation(),
                        kernelTree.FilePath,
                        typeSymbol.Name,
                        entityLocation.SourceTree?.FilePath ?? "<unknown>"));
                }
            }
        });
    }

    private static bool HasSegment(string? filePath)
    {
        if (string.IsNullOrEmpty(filePath))
        {
            return false;
        }

        var parts = filePath.Split('/', '\\', StringSplitOptions.RemoveEmptyEntries);
        return parts.Any(p => EntityFolderSegments.Contains(p, StringComparer.OrdinalIgnoreCase));
    }
}
