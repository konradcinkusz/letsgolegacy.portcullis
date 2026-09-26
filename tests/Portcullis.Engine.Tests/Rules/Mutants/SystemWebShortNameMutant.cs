using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;
using Portcullis.Rules;

namespace Portcullis.Engine.Tests.Rules.Mutants;

/// <summary>
/// Mutation-pass variant of <see cref="SystemWebUsageAnalyzer"/> (docs/MUTATIONS.md section
/// 8, variant "system-web-by-short-name"): the rule written the way a text search would
/// write it — a name counts as System.Web when it is spelled like one of System.Web's
/// best-known types, without asking what it binds to. It reports ASP.NET Core's own
/// <c>HttpContext</c>, which is the false positive the semantic rule exists to avoid, so
/// its test is inverted like the convention-coverage one: the real rule stays silent, the
/// mutant fires. Reuses the real <see cref="SystemWebUsageAnalyzer.SystemWebRule"/>
/// descriptor.
/// </summary>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class SystemWebShortNameMutant : DiagnosticAnalyzer
{
    private static readonly string[] SystemWebTypeNames = ["HttpContext", "HttpRequest", "HttpResponse", "HttpCookie"];

    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics { get; } =
        ImmutableArray.Create(SystemWebUsageAnalyzer.SystemWebRule);

    public override void Initialize(AnalysisContext context)
    {
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.EnableConcurrentExecution();
        context.RegisterSyntaxNodeAction(AnalyzeName, SyntaxKind.IdentifierName);
    }

    private static void AnalyzeName(SyntaxNodeAnalysisContext context)
    {
        var name = (IdentifierNameSyntax)context.Node;
        if (SystemWebTypeNames.Contains(name.Identifier.ValueText))
        {
            context.ReportDiagnostic(Diagnostic.Create(
                SystemWebUsageAnalyzer.SystemWebRule, name.GetLocation(), "System.Web." + name.Identifier.ValueText));
        }
    }
}
