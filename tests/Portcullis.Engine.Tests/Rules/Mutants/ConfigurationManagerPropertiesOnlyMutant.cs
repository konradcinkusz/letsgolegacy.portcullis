using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Operations;
using Portcullis.Rules;

namespace Portcullis.Engine.Tests.Rules.Mutants;

/// <summary>
/// Mutation-pass variant of <see cref="ConfigurationManagerAnalyzer"/> (docs/MUTATIONS.md
/// section 8, variant "configuration-manager-properties-only"). It registers for property
/// references only — <c>AppSettings</c> and <c>ConnectionStrings</c> are properties — and
/// forgets that <c>GetSection</c> is a method, so a custom configuration section read the
/// .NET Framework way is never reported. Otherwise the real rule's logic; reuses the real
/// <see cref="ConfigurationManagerAnalyzer.ConfigurationManagerRule"/> descriptor.
/// </summary>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class ConfigurationManagerPropertiesOnlyMutant : DiagnosticAnalyzer
{
    private static readonly ImmutableHashSet<string> LegacyConfigurationTypes = ImmutableHashSet.Create(
        StringComparer.Ordinal,
        "System.Configuration.ConfigurationManager",
        "System.Web.Configuration.WebConfigurationManager",
        "System.Configuration.ConfigurationSettings");

    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics { get; } =
        ImmutableArray.Create(ConfigurationManagerAnalyzer.ConfigurationManagerRule);

    public override void Initialize(AnalysisContext context)
    {
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.EnableConcurrentExecution();
        context.RegisterOperationAction(Analyze, OperationKind.PropertyReference); // the mutation: no Invocation, no MethodReference
    }

    private static void Analyze(OperationAnalysisContext context)
    {
        var property = ((IPropertyReferenceOperation)context.Operation).Property;
        if (property.IsStatic
            && LegacyConfigurationTypes.Contains(MigrationSymbols.MetadataName(property.ContainingType))
            && !MigrationSymbols.IsInsideNameOf(context.Operation))
        {
            context.ReportDiagnostic(Diagnostic.Create(
                ConfigurationManagerAnalyzer.ConfigurationManagerRule,
                context.Operation.Syntax.GetLocation(),
                context.Operation.Syntax.ToString(),
                MigrationSymbols.MetadataName(property.ContainingType)));
        }
    }
}
