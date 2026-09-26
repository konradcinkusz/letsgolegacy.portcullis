using System.Collections.Immutable;
using Portcullis.Rules;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;

namespace Portcullis.Engine.Tests.Rules.Mutants;

/// <summary>
/// Mutation-pass variant of <see cref="ObservabilityBuildTimeAnalyzer"/>
/// (docs/MUTATIONS.md, variant "host-builder-factory-narrowed"). The real rule
/// recognizes both "CreateBuilder" (WebApplication) and "CreateApplicationBuilder"
/// (Host) as service entry points; this mutant recognizes only "CreateBuilder" — so a
/// Host.CreateApplicationBuilder-based worker/job Program.cs (the reference consumer app's
/// Consumer.Jobs.PromotionExpiry, real and current) is never even recognized as a
/// service entry point, and a missing AddServiceDefaults() call in one goes uncaught.
/// </summary>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class HostBuilderFactoryNarrowedMutant : DiagnosticAnalyzer
{
    private static readonly string[] HostBuilderFactoryMethods = ["CreateBuilder"]; // the mutation: should also include "CreateApplicationBuilder"

    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics { get; } =
        ImmutableArray.Create(ObservabilityBuildTimeAnalyzer.MissingServiceDefaultsRule);

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
            if (!IsProgramFile(tree.FilePath))
            {
                continue;
            }

            var root = tree.GetRoot(context.CancellationToken);

            var builderCall = root.DescendantNodes()
                .OfType<InvocationExpressionSyntax>()
                .FirstOrDefault(inv =>
                    inv.Expression is MemberAccessExpressionSyntax member
                    && HostBuilderFactoryMethods.Contains(member.Name.Identifier.Text, StringComparer.Ordinal));

            if (builderCall is null)
            {
                continue;
            }

            var callsAddServiceDefaults = root.DescendantNodes()
                .OfType<InvocationExpressionSyntax>()
                .Any(inv => (inv.Expression as MemberAccessExpressionSyntax)?.Name.Identifier.Text == "AddServiceDefaults");

            if (callsAddServiceDefaults)
            {
                continue;
            }

            context.ReportDiagnostic(Diagnostic.Create(
                ObservabilityBuildTimeAnalyzer.MissingServiceDefaultsRule,
                builderCall.GetLocation(),
                tree.FilePath));
        }
    }

    private static bool IsProgramFile(string? filePath) =>
        filePath is not null
        && string.Equals(Path.GetFileName(filePath), "Program.cs", StringComparison.OrdinalIgnoreCase);
}
