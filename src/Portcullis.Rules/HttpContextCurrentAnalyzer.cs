using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Operations;

namespace Portcullis.Rules;

/// <summary>
/// Migration rule <c>PORTCULLIS_MIG_HTTPCONTEXT_CURRENT</c>: a read or write of the static
/// <c>System.Web.HttpContext.Current</c>. It is the one System.Web idiom that outlives the
/// namespace itself: code that reaches for the current request through a static works
/// only while it runs on a request's own flow, hides a dependency on the web layer inside
/// code that looks framework-free, and on ASP.NET Core exists only through the System.Web
/// adapters. The replacement is a dependency — <c>IHttpContextAccessor</c>, or better the
/// values themselves — passed in. docs/rules/MIGRATION.md has the reasoning and the gaps.
///
/// Implemented on the operation tree rather than on syntax: every property reference the
/// compiler bound is checked for being the static <c>Current</c> declared by the type whose
/// metadata name is <c>System.Web.HttpContext</c>. So an alias, a <c>using static</c>
/// import and the fully-qualified form are all caught, while a team's own static
/// <c>HttpContext.Current</c> in another namespace, <c>nameof(HttpContext.Current)</c> and
/// every instance member of <c>HttpContext</c> are not. Deliberately independent of
/// <see cref="SystemWebUsageAnalyzer"/>: a line reading <c>HttpContext.Current</c> gets both
/// diagnostics, one for depending on System.Web at all and one for reading request state
/// through a static, because a team can retire the first (by moving to the adapters) and
/// still owe the second.
/// </summary>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class HttpContextCurrentAnalyzer : DiagnosticAnalyzer
{
    private const string Id = "PORTCULLIS_MIG_HTTPCONTEXT_CURRENT";
    private const string Title = "Request state read through System.Web.HttpContext.Current";
    private const string MessageFormat =
        "'{0}' reaches the current request through the static System.Web.HttpContext.Current, " +
        "which works only on a request's own flow and exists on ASP.NET Core only through the " +
        "System.Web adapters. Inject IHttpContextAccessor, or pass the values in, instead.";

    public static readonly DiagnosticDescriptor HttpContextCurrentRule = new(
        Id, Title, MessageFormat, MigrationSymbols.Category, DiagnosticSeverity.Error, isEnabledByDefault: true);

    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics { get; } =
        ImmutableArray.Create(HttpContextCurrentRule);

    public override void Initialize(AnalysisContext context)
    {
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.EnableConcurrentExecution();
        context.RegisterCompilationStartAction(start =>
        {
            var isExempt = MigrationSymbols.ExemptionFor(PortcullisConventions.From(start.Options));
            start.RegisterOperationAction(c => Analyze(c, isExempt), OperationKind.PropertyReference);
        });
    }

    private static void Analyze(OperationAnalysisContext context, Func<SyntaxTree, bool> isExempt)
    {
        var reference = (IPropertyReferenceOperation)context.Operation;
        if (!IsHttpContextCurrent(reference.Property)
            || MigrationSymbols.IsInsideNameOf(reference)
            || isExempt(reference.Syntax.SyntaxTree))
        {
            return;
        }

        context.ReportDiagnostic(Diagnostic.Create(
            HttpContextCurrentRule, reference.Syntax.GetLocation(), reference.Syntax.ToString()));
    }

    private static bool IsHttpContextCurrent(IPropertySymbol property) =>
        property.IsStatic
        && property.Name == "Current"
        && MigrationSymbols.MetadataName(property.ContainingType) == "System.Web.HttpContext";
}
