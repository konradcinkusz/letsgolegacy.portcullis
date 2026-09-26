using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Operations;
using Portcullis.Rules;

namespace Portcullis.Engine.Tests.Rules.Mutants;

/// <summary>
/// Mutation-pass variant of <see cref="HttpContextCurrentAnalyzer"/> (docs/MUTATIONS.md
/// section 8, variant "httpcontext-current-wrong-owner"). The static <c>Current</c> is
/// looked for on <c>System.Web.HttpContextBase</c> — the abstraction a migration guide
/// talks about most — instead of <c>System.Web.HttpContext</c>, which is the only type that
/// declares it. The check can never be true, so no read of <c>HttpContext.Current</c> is
/// ever reported. Otherwise the real rule's logic; reuses the real
/// <see cref="HttpContextCurrentAnalyzer.HttpContextCurrentRule"/> descriptor.
/// </summary>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class HttpContextCurrentOwnerMutant : DiagnosticAnalyzer
{
    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics { get; } =
        ImmutableArray.Create(HttpContextCurrentAnalyzer.HttpContextCurrentRule);

    public override void Initialize(AnalysisContext context)
    {
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.EnableConcurrentExecution();
        context.RegisterOperationAction(Analyze, OperationKind.PropertyReference);
    }

    private static void Analyze(OperationAnalysisContext context)
    {
        var reference = (IPropertyReferenceOperation)context.Operation;
        var property = reference.Property;
        if (property.IsStatic
            && property.Name == "Current"
            && MigrationSymbols.MetadataName(property.ContainingType) == "System.Web.HttpContextBase" // the mutation
            && !MigrationSymbols.IsInsideNameOf(reference))
        {
            context.ReportDiagnostic(Diagnostic.Create(
                HttpContextCurrentAnalyzer.HttpContextCurrentRule, reference.Syntax.GetLocation(), reference.Syntax.ToString()));
        }
    }
}
