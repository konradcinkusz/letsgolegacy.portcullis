using System.Collections.Immutable;
using Portcullis.Rules;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;

namespace Portcullis.Engine.Tests.Rules.Mutants;

/// <summary>
/// Mutation-pass variant of <see cref="PersistencePortabilityAnalyzer"/>'s
/// <c>PORTCULLIS_P4_ENSURE_CREATED_OUTSIDE_TEST</c> diagnostic (docs/MUTATIONS.md, variant
/// "ensure-created-async-variant-dropped"). The real rule matches both "EnsureCreated"
/// and "EnsureCreatedAsync"; this mutant matches only the synchronous name — exactly the
/// variant real code never actually calls (the reference consumer app's own DatabaseExtensions.cs, and every
/// async-first EF Core codebase, calls EnsureCreatedAsync), so the mutant silently stops
/// catching the realistic case entirely rather than merely narrowing it.
/// </summary>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class EnsureCreatedAsyncVariantDroppedMutant : DiagnosticAnalyzer
{
    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics { get; } =
        ImmutableArray.Create(PersistencePortabilityAnalyzer.EnsureCreatedOutsideTestRule);

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
                if (invocation.Expression is not MemberAccessExpressionSyntax
                    {
                        Name.Identifier.Text: "EnsureCreated", // the mutation: should also match "EnsureCreatedAsync"
                        Expression: MemberAccessExpressionSyntax { Name.Identifier.Text: "Database" },
                    } memberAccess)
                {
                    continue;
                }

                if (IsGuardedByIsInMemoryCheck(invocation))
                {
                    continue;
                }

                context.ReportDiagnostic(Diagnostic.Create(
                    PersistencePortabilityAnalyzer.EnsureCreatedOutsideTestRule,
                    invocation.GetLocation(),
                    memberAccess.Name.Identifier.Text));
            }
        }
    }

    private static bool IsGuardedByIsInMemoryCheck(SyntaxNode node) =>
        node.Ancestors()
            .OfType<IfStatementSyntax>()
            .Any(ifStatement => ifStatement.Condition
                .DescendantNodesAndSelf()
                .OfType<InvocationExpressionSyntax>()
                .Any(inv => (inv.Expression as MemberAccessExpressionSyntax)?.Name.Identifier.Text == "IsInMemory"));
}
