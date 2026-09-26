using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Operations;

namespace Portcullis.Rules;

/// <summary>
/// Migration rule <c>PORTCULLIS_MIG_CONFIGURATION_MANAGER</c>: settings read through the
/// .NET Framework configuration API — <c>System.Configuration.ConfigurationManager</c>
/// (<c>AppSettings</c>, <c>ConnectionStrings</c>, <c>GetSection</c>, and every other static
/// member), its ASP.NET twin <c>System.Web.Configuration.WebConfigurationManager</c>, and the
/// long-obsolete <c>System.Configuration.ConfigurationSettings</c> — instead of
/// <c>IOptions&lt;T&gt;</c> or <c>IConfiguration</c>.
///
/// This is the migration defect that survives every build: the
/// <c>System.Configuration.ConfigurationManager</c> package makes the code compile on
/// modern .NET, but there it reads <c>&lt;app&gt;.dll.config</c>, not <c>web.config</c> and
/// never <c>appsettings.json</c>, so a value that used to arrive comes back null at run
/// time. docs/rules/MIGRATION.md has the full reasoning.
///
/// Semantic: every static property reference, method call and method-group reference the
/// compiler bound is checked for being declared by one of the three types, identified by
/// metadata name. <c>Microsoft.Extensions.Configuration.ConfigurationManager</c> — the
/// modern class <c>WebApplicationBuilder.Configuration</c> returns, which a text search for
/// "ConfigurationManager" would flag — is a different type and is never reported; neither
/// is a team's own <c>ConfigurationManager</c>, nor <c>nameof(ConfigurationManager.AppSettings)</c>.
/// </summary>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class ConfigurationManagerAnalyzer : DiagnosticAnalyzer
{
    private static readonly ImmutableHashSet<string> LegacyConfigurationTypes = ImmutableHashSet.Create(
        StringComparer.Ordinal,
        "System.Configuration.ConfigurationManager",
        "System.Web.Configuration.WebConfigurationManager",
        "System.Configuration.ConfigurationSettings");

    private const string Id = "PORTCULLIS_MIG_CONFIGURATION_MANAGER";
    private const string Title = "Settings read through the .NET Framework configuration API";
    private const string MessageFormat =
        "'{0}' reads settings through {1}. On modern .NET that API reads <app>.dll.config — " +
        "not web.config, never appsettings.json — so a value that used to arrive can come " +
        "back null at run time with no build error. Bind IOptions<T> or read IConfiguration instead.";

    public static readonly DiagnosticDescriptor ConfigurationManagerRule = new(
        Id, Title, MessageFormat, MigrationSymbols.Category, DiagnosticSeverity.Error, isEnabledByDefault: true);

    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics { get; } =
        ImmutableArray.Create(ConfigurationManagerRule);

    public override void Initialize(AnalysisContext context)
    {
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.EnableConcurrentExecution();
        context.RegisterCompilationStartAction(start =>
        {
            var isExempt = MigrationSymbols.ExemptionFor(PortcullisConventions.From(start.Options));
            start.RegisterOperationAction(
                c => Analyze(c, isExempt),
                OperationKind.PropertyReference, OperationKind.Invocation, OperationKind.MethodReference);
        });
    }

    private static void Analyze(OperationAnalysisContext context, Func<SyntaxTree, bool> isExempt)
    {
        var operation = context.Operation;
        ISymbol? member = operation switch
        {
            IPropertyReferenceOperation property => property.Property,
            IInvocationOperation invocation => invocation.TargetMethod,
            IMethodReferenceOperation method => method.Method,
            _ => null,
        };

        if (member is not { IsStatic: true }
            || !LegacyConfigurationTypes.Contains(MigrationSymbols.MetadataName(member.ContainingType))
            || MigrationSymbols.IsInsideNameOf(operation)
            || isExempt(operation.Syntax.SyntaxTree))
        {
            return;
        }

        context.ReportDiagnostic(Diagnostic.Create(
            ConfigurationManagerRule,
            operation.Syntax.GetLocation(),
            operation.Syntax.ToString(),
            MigrationSymbols.MetadataName(member.ContainingType)));
    }
}
