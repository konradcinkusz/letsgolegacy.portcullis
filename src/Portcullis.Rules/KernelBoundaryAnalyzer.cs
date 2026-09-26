using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Text;

namespace Portcullis.Rules;

/// <summary>
/// architecture-standards P2: shared code is a shared *kernel*, not a shared *domain*.
/// Two mechanical checks, exactly as docs/SPEC.md section 5's P2 entry describes: an
/// ~800-line ceiling on kernel-folder source, and a dependency-direction rule forbidding
/// the kernel from referencing entity types.
///
/// "Kernel folder" and "entity folder" are both name-convention heuristics (folder
/// segments, matched case-insensitively) rather than attribute- or namespace-driven,
/// because nothing in the target source is required to declare either role explicitly —
/// the same convention architecture-standards itself uses when it names
/// `ServiceDefaults` and `Domain` as the shapes these principles apply to.
///
/// Which names those are is now read from <see cref="PortcullisConventions"/> rather than
/// baked in here, so a team whose kernel is called `Common` can say so. The defaults are
/// the previously-hardcoded lists verbatim, so an unconfigured consumer sees no change.
/// A convention that matches nothing at all is reported by
/// <see cref="ConventionCoverageAnalyzer"/> — this rule's early return below is exactly
/// the silence that made a misconfigured gate look like a passing one.
/// </summary>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class KernelBoundaryAnalyzer : DiagnosticAnalyzer
{
    public const int LocCeiling = 800;

    public static readonly DiagnosticDescriptor LocCeilingRule = new(
        id: "PORTCULLIS_P2_KERNEL_LOC_CEILING",
        title: "Shared kernel exceeds its line-count ceiling",
        messageFormat:
            "Kernel folder '{0}' totals {1} lines across its *.cs files, exceeding the " +
            "{2}-line ceiling (architecture-standards P2). Largest contributor: '{3}' ({4} lines).",
        category: "P2",
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    public static readonly DiagnosticDescriptor KernelEntityReferenceRule = new(
        id: "PORTCULLIS_P2_KERNEL_ENTITY_REFERENCE",
        title: "Shared kernel references a business entity type",
        messageFormat:
            "Kernel file '{0}' references '{1}', a type declared under a Domain/Entities " +
            "folder ('{2}'). The shared kernel must hold no entity, DTO, or business type " +
            "(architecture-standards P2).",
        category: "P2",
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics { get; } =
        ImmutableArray.Create(LocCeilingRule, KernelEntityReferenceRule);

    public override void Initialize(AnalysisContext context)
    {
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.EnableConcurrentExecution();
        context.RegisterCompilationAction(AnalyzeCompilation);
    }

    private static void AnalyzeCompilation(CompilationAnalysisContext context)
    {
        var conventions = PortcullisConventions.From(context.Options);
        var compilation = context.Compilation;
        var kernelTrees = compilation.SyntaxTrees
            .Where(t => PathConventions.HasSegment(conventions.Relative(t.FilePath), conventions.KernelFolders))
            .ToList();

        if (kernelTrees.Count == 0)
        {
            return;
        }

        CheckLocCeiling(context, kernelTrees, conventions);

        foreach (var tree in kernelTrees)
        {
            CheckEntityReferences(context, compilation, tree, conventions);
        }
    }

    private static void CheckLocCeiling(
        CompilationAnalysisContext context, List<SyntaxTree> kernelTrees, PortcullisConventions conventions)
    {
        var counts = kernelTrees
            .Select(t => (Tree: t, Lines: t.GetText().Lines.Count))
            .ToList();
        var total = counts.Sum(c => c.Lines);
        if (total <= conventions.KernelLineCeiling)
        {
            return;
        }

        var largest = counts.OrderByDescending(c => c.Lines).First();
        var folder = PathConventions.MatchedSegment(conventions.Relative(largest.Tree.FilePath), conventions.KernelFolders)
            ?? largest.Tree.FilePath;
        var text = largest.Tree.GetText();
        var location = Location.Create(largest.Tree, text.Lines.Count > 0 ? text.Lines[0].Span : new TextSpan(0, 0));

        context.ReportDiagnostic(Diagnostic.Create(
            LocCeilingRule,
            location,
            folder,
            total,
            conventions.KernelLineCeiling,
            largest.Tree.FilePath,
            largest.Lines));
    }

    private static void CheckEntityReferences(
        CompilationAnalysisContext context, Compilation compilation, SyntaxTree kernelTree,
        PortcullisConventions conventions)
    {
        var semanticModel = compilation.GetSemanticModel(kernelTree);
        var root = kernelTree.GetRoot(context.CancellationToken);
        var reported = new HashSet<string>(StringComparer.Ordinal);

        foreach (var name in root.DescendantNodes().OfType<SimpleNameSyntax>())
        {
            var symbol = semanticModel.GetSymbolInfo(name, context.CancellationToken).Symbol;
            var typeSymbol = symbol switch
            {
                INamedTypeSymbol t => t,
                IMethodSymbol { MethodKind: MethodKind.Constructor } m => m.ContainingType,
                IPropertySymbol p => p.Type as INamedTypeSymbol,
                IFieldSymbol f => f.Type as INamedTypeSymbol,
                ILocalSymbol l => l.Type as INamedTypeSymbol,
                IParameterSymbol pa => pa.Type as INamedTypeSymbol,
                _ => null,
            };

            if (typeSymbol is null || reported.Contains(typeSymbol.Name))
            {
                continue;
            }

            var entityLocation = typeSymbol.Locations
                .FirstOrDefault(l => l.IsInSource
                    && PathConventions.HasSegment(conventions.Relative(l.SourceTree?.FilePath), conventions.EntityFolders));

            if (entityLocation is null)
            {
                continue;
            }

            reported.Add(typeSymbol.Name);
            context.ReportDiagnostic(Diagnostic.Create(
                KernelEntityReferenceRule,
                name.GetLocation(),
                kernelTree.FilePath,
                typeSymbol.Name,
                entityLocation.SourceTree?.FilePath ?? "<unknown>"));
        }
    }
}
