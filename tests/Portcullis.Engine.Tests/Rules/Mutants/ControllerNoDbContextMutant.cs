using System.Collections.Immutable;
using Portcullis.Rules;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;

namespace Portcullis.Engine.Tests.Rules.Mutants;

/// <summary>
/// Mutation-pass variant of <see cref="ProgramManifestLayeringAnalyzer"/>'s
/// controller-no-DbContext check (docs/MUTATIONS.md, variant
/// "controller-dbcontext-exact-match"). The real rule matches any type name *ending in*
/// "DbContext" (so it catches concrete derived contexts like "AdvertsDbContext"); this
/// mutant requires an exact match on the literal name "DbContext", which almost no real
/// controller ever references directly (concrete DbContext subclasses always have a
/// service-specific prefix) — so real violations sail through undetected. Reuses the
/// real <see cref="ProgramManifestLayeringAnalyzer.ControllerNoDbContextRule"/>
/// descriptor.
/// </summary>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class ControllerNoDbContextMutant : DiagnosticAnalyzer
{
    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics { get; } =
        ImmutableArray.Create(ProgramManifestLayeringAnalyzer.ControllerNoDbContextRule);

    public override void Initialize(AnalysisContext context)
    {
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.EnableConcurrentExecution();
        context.RegisterCompilationAction(ctx =>
        {
            foreach (var tree in ctx.Compilation.SyntaxTrees)
            {
                var root = tree.GetRoot(ctx.CancellationToken);
                foreach (var classDecl in root.DescendantNodes().OfType<ClassDeclarationSyntax>())
                {
                    if (!IsController(classDecl))
                    {
                        continue;
                    }

                    foreach (var typeSyntax in classDecl.DescendantNodes().OfType<TypeSyntax>())
                    {
                        var name = TypeNameOf(typeSyntax);
                        if (name != "DbContext") // exact match only — the mutation
                        {
                            continue;
                        }

                        ctx.ReportDiagnostic(Diagnostic.Create(
                            ProgramManifestLayeringAnalyzer.ControllerNoDbContextRule,
                            typeSyntax.GetLocation(),
                            classDecl.Identifier.Text,
                            name));
                    }
                }
            }
        });
    }

    private static bool IsController(ClassDeclarationSyntax classDecl)
    {
        if (classDecl.Identifier.Text.EndsWith("Controller", StringComparison.Ordinal))
        {
            return true;
        }

        if (classDecl.AttributeLists
            .SelectMany(al => al.Attributes)
            .Any(a => AttributeNameOf(a.Name).Contains("ApiController", StringComparison.Ordinal)))
        {
            return true;
        }

        return classDecl.BaseList?.Types
            .Select(t => TypeNameOf(t.Type))
            .Any(n => n is "ControllerBase" or "Controller") == true;
    }

    private static string AttributeNameOf(NameSyntax name) => name switch
    {
        QualifiedNameSyntax q => q.Right.Identifier.Text,
        SimpleNameSyntax s => s.Identifier.Text,
        _ => name.ToString(),
    };

    private static string? TypeNameOf(TypeSyntax type) => type switch
    {
        IdentifierNameSyntax id => id.Identifier.Text,
        GenericNameSyntax g => g.Identifier.Text,
        QualifiedNameSyntax q => TypeNameOf(q.Right),
        _ => null,
    };
}
