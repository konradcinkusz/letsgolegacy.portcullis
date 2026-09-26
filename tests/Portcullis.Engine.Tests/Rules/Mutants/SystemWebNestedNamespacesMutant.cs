using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;
using Portcullis.Rules;

namespace Portcullis.Engine.Tests.Rules.Mutants;

/// <summary>
/// Mutation-pass variant of <see cref="SystemWebUsageAnalyzer"/> (docs/MUTATIONS.md section
/// 8, variant "system-web-nested-namespaces-dropped"). The namespace test asks whether a
/// namespace <em>is</em> System.Web instead of whether it is System.Web or nested in it —
/// the difference between <c>== "System.Web"</c> and a prefix test. System.Web.SessionState,
/// .Caching, .Mvc and every other nested namespace stop counting, so a using directive for
/// one and every type reached through it go unreported. Otherwise the real rule's logic,
/// with the default configuration; reuses the real
/// <see cref="SystemWebUsageAnalyzer.SystemWebRule"/> descriptor.
/// </summary>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class SystemWebNestedNamespacesMutant : DiagnosticAnalyzer
{
    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics { get; } =
        ImmutableArray.Create(SystemWebUsageAnalyzer.SystemWebRule);

    public override void Initialize(AnalysisContext context)
    {
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.EnableConcurrentExecution();
        context.RegisterSyntaxNodeAction(AnalyzeUsingDirective, SyntaxKind.UsingDirective);
        context.RegisterSyntaxNodeAction(AnalyzeName, SyntaxKind.IdentifierName, SyntaxKind.GenericName);
    }

    // The mutation: equality where the real rule tests "this namespace or one nested in it".
    private static bool IsSystemWebNamespace(INamespaceSymbol ns) => MigrationSymbols.NamespaceName(ns) == "System.Web";

    private static bool IsSystemWebType(INamedTypeSymbol type) => IsSystemWebNamespace(type.ContainingNamespace);

    private static bool IsAllowed(INamedTypeSymbol type) =>
        PortcullisConventions.Default.SystemWebAllowedTypes.Contains(MigrationSymbols.MetadataName(type));

    private static bool IsHomeOfAnAllowedType(INamespaceSymbol ns)
    {
        var prefix = MigrationSymbols.QualifiedPrefix(ns);
        return PortcullisConventions.Default.SystemWebAllowedTypes.Any(type =>
            type.StartsWith(prefix, StringComparison.Ordinal) && !type.Substring(prefix.Length).Contains('.'));
    }

    private static void AnalyzeUsingDirective(SyntaxNodeAnalysisContext context)
    {
        var directive = (UsingDirectiveSyntax)context.Node;
        var target = directive.NamespaceOrType;
        TypeSyntax? bound = target;
        var symbol = context.SemanticModel.GetSymbolInfo(bound, context.CancellationToken).Symbol;
        while (symbol == null && bound is QualifiedNameSyntax)
        {
            bound = ((QualifiedNameSyntax)bound).Left;
            symbol = context.SemanticModel.GetSymbolInfo(bound, context.CancellationToken).Symbol;
        }

        var legacy = symbol switch
        {
            INamespaceSymbol ns => IsSystemWebNamespace(ns) && !(bound == target && IsHomeOfAnAllowedType(ns)),
            INamedTypeSymbol type => IsSystemWebType(type) && !IsAllowed(type),
            _ => false,
        };

        if (legacy)
        {
            context.ReportDiagnostic(Diagnostic.Create(SystemWebUsageAnalyzer.SystemWebRule, directive.GetLocation(), target.ToString()));
        }
    }

    private static void AnalyzeName(SyntaxNodeAnalysisContext context)
    {
        var name = (SimpleNameSyntax)context.Node;
        SyntaxNode top = name;
        while (top.Parent is QualifiedNameSyntax)
        {
            top = top.Parent;
        }

        if (name.IsPartOfStructuredTrivia() || top.Parent is UsingDirectiveSyntax or BaseNamespaceDeclarationSyntax)
        {
            return;
        }

        var type = context.SemanticModel.GetSymbolInfo(name, context.CancellationToken).Symbol as INamedTypeSymbol;
        if (type != null && IsSystemWebType(type) && !IsAllowed(type))
        {
            context.ReportDiagnostic(Diagnostic.Create(
                SystemWebUsageAnalyzer.SystemWebRule, name.GetLocation(), MigrationSymbols.MetadataName(type)));
        }
    }
}
