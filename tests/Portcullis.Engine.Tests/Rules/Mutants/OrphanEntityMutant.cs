using System.Collections.Immutable;
using Portcullis.Rules;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;

namespace Portcullis.Engine.Tests.Rules.Mutants;

/// <summary>
/// Mutation-pass variant of <see cref="ProgramManifestLayeringAnalyzer"/>'s
/// orphan-entity check (docs/MUTATIONS.md, variant "orphan-entity-self-reference").
/// The real rule excludes the <c>DbSet&lt;T&gt;</c> declaration's own span when
/// counting references to T, because the declaration site is not a "usage" — it is the
/// registration, not a consumer. This mutant forgets that exclusion, so a real entity's
/// own <c>DbSet&lt;T&gt;</c> line always counts as one reference to itself, and no
/// entity can ever be flagged as orphan. Reuses the real
/// <see cref="ProgramManifestLayeringAnalyzer.OrphanEntityRule"/> descriptor.
/// </summary>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class OrphanEntityMutant : DiagnosticAnalyzer
{
    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics { get; } =
        ImmutableArray.Create(ProgramManifestLayeringAnalyzer.OrphanEntityRule);

    public override void Initialize(AnalysisContext context)
    {
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.EnableConcurrentExecution();
        context.RegisterCompilationAction(ctx =>
        {
            var compilation = ctx.Compilation;
            var dbSetEntities = new List<(INamedTypeSymbol Entity, PropertyDeclarationSyntax Property)>();

            foreach (var tree in compilation.SyntaxTrees)
            {
                var semanticModel = compilation.GetSemanticModel(tree);
                var root = tree.GetRoot(ctx.CancellationToken);

                foreach (var classDecl in root.DescendantNodes().OfType<ClassDeclarationSyntax>())
                {
                    var isDbContext = classDecl.BaseList?.Types
                        .Select(t => TypeNameOf(t.Type))
                        .Any(n => n == "DbContext") == true;
                    if (!isDbContext)
                    {
                        continue;
                    }

                    foreach (var property in classDecl.Members.OfType<PropertyDeclarationSyntax>())
                    {
                        if (property.Type is not GenericNameSyntax { Identifier.Text: "DbSet" } generic
                            || generic.TypeArgumentList.Arguments.Count != 1)
                        {
                            continue;
                        }

                        var typeArgument = generic.TypeArgumentList.Arguments[0];
                        if (semanticModel.GetSymbolInfo(typeArgument, ctx.CancellationToken).Symbol is not INamedTypeSymbol entity)
                        {
                            continue;
                        }

                        if (!entity.Locations.Any(l => l.IsInSource))
                        {
                            continue;
                        }

                        dbSetEntities.Add((entity, property));
                    }
                }
            }

            foreach (var (entity, property) in dbSetEntities)
            {
                if (IsReferencedAnywhere(ctx, compilation, entity))
                {
                    continue;
                }

                var entityLocation = entity.Locations.First(l => l.IsInSource);
                var dbContextClass = (ClassDeclarationSyntax)property.Parent!;
                ctx.ReportDiagnostic(Diagnostic.Create(
                    ProgramManifestLayeringAnalyzer.OrphanEntityRule,
                    entityLocation,
                    entity.Name,
                    dbContextClass.Identifier.Text,
                    property.Identifier.Text));
            }
        });
    }

    // The mutation: no longer excludes the DbSet<T> declaration's own span, so it
    // counts as a reference to itself and every entity looks "used".
    private static bool IsReferencedAnywhere(CompilationAnalysisContext ctx, Compilation compilation, INamedTypeSymbol entity)
    {
        var entityTree = entity.Locations.First(l => l.IsInSource).SourceTree!;

        foreach (var tree in compilation.SyntaxTrees)
        {
            if (tree == entityTree)
            {
                continue;
            }

            var semanticModel = compilation.GetSemanticModel(tree);
            var root = tree.GetRoot(ctx.CancellationToken);

            foreach (var name in root.DescendantNodes().OfType<SimpleNameSyntax>())
            {
                if (!string.Equals(name.Identifier.Text, entity.Name, StringComparison.Ordinal))
                {
                    continue;
                }

                var symbol = semanticModel.GetSymbolInfo(name, ctx.CancellationToken).Symbol;
                if (SymbolEqualityComparer.Default.Equals(symbol, entity))
                {
                    return true;
                }
            }
        }

        return false;
    }

    private static string? TypeNameOf(TypeSyntax type) => type switch
    {
        IdentifierNameSyntax id => id.Identifier.Text,
        GenericNameSyntax g => g.Identifier.Text,
        QualifiedNameSyntax q => TypeNameOf(q.Right),
        _ => null,
    };
}
