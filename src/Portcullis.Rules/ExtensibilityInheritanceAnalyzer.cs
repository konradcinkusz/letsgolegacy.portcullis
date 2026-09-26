using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;

namespace Portcullis.Rules;

/// <summary>
/// architecture-standards P10: a new algorithm, provider, or step is a class
/// implementing an interface and one DI registration line — no base class to derive
/// from, no framework to satisfy. This analyzer flags a class inheriting from another
/// class that is itself declared in the scanned source.
///
/// Restricting the check to source-declared base types (<c>Locations.Any(IsInSource)</c>)
/// is deliberate, not incidental: this milestone's scanner (docs/BOOTSTRAP.md's scope
/// note) parses target source directly and references only corelib, so a base type from
/// ASP.NET Core, EF Core, or any other unreferenced framework assembly never resolves to
/// a usable symbol at all — it comes back as an unresolved/error type, which this rule
/// already excludes via <c>TypeKind == TypeKind.Class</c>. That is what keeps this rule
/// from flagging `: ControllerBase` or `: DbContext`, which are legitimate framework
/// extension points, not the "own ModuleBase" pattern P10 exists to catch.
/// </summary>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class ExtensibilityInheritanceAnalyzer : DiagnosticAnalyzer
{
    public static readonly DiagnosticDescriptor CustomBaseClassRule = new(
        id: "PORTCULLIS_P10_CUSTOM_BASE_CLASS",
        title: "Class inherits from a custom base class",
        messageFormat:
            "'{0}' inherits from '{1}', a class declared in this codebase. Extend via an " +
            "interface implemented by '{0}' and registered in DI instead of a base class " +
            "to derive from (architecture-standards P10).",
        category: "P10",
        defaultSeverity: DiagnosticSeverity.Warning,
        isEnabledByDefault: true);

    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics { get; } =
        ImmutableArray.Create(CustomBaseClassRule);

    public override void Initialize(AnalysisContext context)
    {
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.EnableConcurrentExecution();
        context.RegisterSyntaxNodeAction(Analyze, SyntaxKind.ClassDeclaration);
    }

    private static void Analyze(SyntaxNodeAnalysisContext context)
    {
        var classDecl = (ClassDeclarationSyntax)context.Node;
        if (classDecl.BaseList is null)
        {
            return;
        }

        var semanticModel = context.SemanticModel;

        foreach (var baseType in classDecl.BaseList.Types)
        {
            if (semanticModel.GetSymbolInfo(baseType.Type, context.CancellationToken).Symbol is not INamedTypeSymbol symbol)
            {
                continue;
            }

            if (symbol.TypeKind != TypeKind.Class
                || symbol.SpecialType == SpecialType.System_Object
                || !symbol.Locations.Any(l => l.IsInSource))
            {
                continue;
            }

            context.ReportDiagnostic(Diagnostic.Create(
                CustomBaseClassRule,
                baseType.GetLocation(),
                classDecl.Identifier.Text,
                symbol.Name));
        }
    }
}
