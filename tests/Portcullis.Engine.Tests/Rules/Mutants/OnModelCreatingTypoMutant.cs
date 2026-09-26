using System.Collections.Immutable;
using Portcullis.Rules;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;

namespace Portcullis.Engine.Tests.Rules.Mutants;

/// <summary>
/// Mutation-pass variant of <see cref="PersistencePortabilityAnalyzer"/>'s
/// <c>PORTCULLIS_P4_SEED_DATA_IN_MODEL</c> diagnostic (docs/MUTATIONS.md, variant
/// "on-model-creating-typo"). The real rule checks the containing method's name against
/// "OnModelCreating" (EF Core's actual override); this mutant checks "OnModelCreated" —
/// missing "ing" — which never matches any real EF Core DbContext override, so the
/// condition can never be true and no HasData-in-model violation is ever flagged.
/// </summary>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class OnModelCreatingTypoMutant : DiagnosticAnalyzer
{
    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics { get; } =
        ImmutableArray.Create(PersistencePortabilityAnalyzer.SeedDataInModelRule);

    public override void Initialize(AnalysisContext context)
    {
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.EnableConcurrentExecution();
        context.RegisterCompilationAction(AnalyzeCompilation);
    }

    private static void AnalyzeCompilation(CompilationAnalysisContext context)
    {
        foreach (var tree in context.Compilation.SyntaxTrees)
        {
            var root = tree.GetRoot(context.CancellationToken);
            foreach (var invocation in root.DescendantNodes().OfType<InvocationExpressionSyntax>())
            {
                var methodName = (invocation.Expression as MemberAccessExpressionSyntax)?.Name.Identifier.Text;
                if (methodName != "HasData")
                {
                    continue;
                }

                var containingMethod = invocation.Ancestors().OfType<MethodDeclarationSyntax>().FirstOrDefault();
                if (containingMethod?.Identifier.Text != "OnModelCreated") // the mutation: should be "OnModelCreating"
                {
                    continue;
                }

                var containingClass = containingMethod.Ancestors().OfType<ClassDeclarationSyntax>().FirstOrDefault();

                context.ReportDiagnostic(Diagnostic.Create(
                    PersistencePortabilityAnalyzer.SeedDataInModelRule,
                    invocation.GetLocation(),
                    containingClass?.Identifier.Text ?? "<unknown>"));
            }
        }
    }
}
