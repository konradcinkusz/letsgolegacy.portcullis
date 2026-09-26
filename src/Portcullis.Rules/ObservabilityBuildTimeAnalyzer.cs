using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;

namespace Portcullis.Rules;

/// <summary>
/// architecture-standards P15: observability is a build-time decision — every service
/// wires OpenTelemetry/health-check plumbing at startup via a shared
/// <c>AddServiceDefaults()</c> extension, not opt-in per environment. A call-presence
/// check, the same technique docs/SPEC.md section 1.2 names for P15 and for the P2a
/// corollary ("AddServiceDefaults() called by every service") — neither of which had an
/// implementation before this rule.
///
/// A file counts as a service entry point when its name is one of the configured entry-point
/// file names (<c>Program.cs</c> by default — see <see cref="PortcullisConventions"/>, added so a
/// codebase whose entry point is <c>Startup.cs</c> is not silently skipped) and it calls
/// <c>WebApplication.CreateBuilder</c> or <c>Host.CreateApplicationBuilder</c> — the two
/// real shapes this session found in the reference consumer app (a web API and a worker/job,
/// respectively). Both already call <c>AddServiceDefaults()</c> in all 7 of the reference consumer app's
/// services, so this rule finds nothing to flag there today — a legitimate compliant
/// result, verified live against a real `portcullis scan`, not a gap in the rule.
/// </summary>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class ObservabilityBuildTimeAnalyzer : DiagnosticAnalyzer
{
    private static readonly string[] HostBuilderFactoryMethods = ["CreateBuilder", "CreateApplicationBuilder"];

    public static readonly DiagnosticDescriptor MissingServiceDefaultsRule = new(
        id: "PORTCULLIS_P15_MISSING_SERVICE_DEFAULTS",
        title: "Service entry point never calls AddServiceDefaults()",
        messageFormat:
            "'{0}' builds a host but never calls 'AddServiceDefaults()'. Observability " +
            "(OpenTelemetry, health checks) is a build-time decision every service wires in " +
            "at startup, not an opt-in left to each environment (architecture-standards P15).",
        category: "P15",
        defaultSeverity: DiagnosticSeverity.Warning,
        isEnabledByDefault: true);

    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics { get; } =
        ImmutableArray.Create(MissingServiceDefaultsRule);

    public override void Initialize(AnalysisContext context)
    {
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.EnableConcurrentExecution();
        context.RegisterCompilationAction(AnalyzeCompilation);
    }

    private static void AnalyzeCompilation(CompilationAnalysisContext context)
    {
        var conventions = PortcullisConventions.From(context.Options);

        foreach (var tree in context.Compilation.SyntaxTrees)
        {
            if (!PathConventions.HasFileName(tree.FilePath, conventions.EntryPointFileNames))
            {
                continue;
            }

            var root = tree.GetRoot(context.CancellationToken);

            var builderCall = root.DescendantNodes()
                .OfType<InvocationExpressionSyntax>()
                .FirstOrDefault(IsHostBuilderFactoryCall);

            if (builderCall is null)
            {
                continue; // not a hostable service entry point (e.g. a plain console tool)
            }

            var callsAddServiceDefaults = root.DescendantNodes()
                .OfType<InvocationExpressionSyntax>()
                .Any(inv => MethodNameOf(inv.Expression) == "AddServiceDefaults");

            if (callsAddServiceDefaults)
            {
                continue;
            }

            context.ReportDiagnostic(Diagnostic.Create(
                MissingServiceDefaultsRule,
                builderCall.GetLocation(),
                tree.FilePath));
        }
    }

    private static bool IsHostBuilderFactoryCall(InvocationExpressionSyntax invocation) =>
        invocation.Expression is MemberAccessExpressionSyntax member
        && HostBuilderFactoryMethods.Contains(member.Name.Identifier.Text, StringComparer.Ordinal);

    private static string? MethodNameOf(ExpressionSyntax expression) => expression switch
    {
        MemberAccessExpressionSyntax m => m.Name.Identifier.Text,
        IdentifierNameSyntax id => id.Identifier.Text,
        _ => null,
    };
}
