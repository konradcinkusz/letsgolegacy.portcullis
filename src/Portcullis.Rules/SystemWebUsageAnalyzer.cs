using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;

namespace Portcullis.Rules;

/// <summary>
/// Migration rule <c>PORTCULLIS_MIG_SYSTEM_WEB</c>: any use of a System.Web namespace or
/// type — a <c>using</c> directive, a qualified name, a type reference — in code that is
/// meant to run on modern .NET, where System.Web does not exist. Such code builds only
/// through a compatibility shim (the System.Web adapters) or a reference left over from
/// the .NET Framework project, so every hit is migration debt the diff did not pay off.
/// docs/rules/MIGRATION.md has the reasoning, examples and the known gaps.
///
/// <b>Semantic, not textual.</b> A name is reported because the symbol it binds to is
/// declared in <c>System.Web</c> or a namespace nested in it (see
/// <see cref="MigrationSymbols"/>): <c>Microsoft.AspNetCore.Http.HttpContext</c> and a
/// team's own <c>HttpContext</c> are never reported, and an alias or a fully-qualified
/// name is. The two System.Web types that ship in modern .NET itself
/// (<c>System.Web.HttpUtility</c> and <c>System.Web.IHtmlString</c>, from
/// <c>System.Web.HttpUtility.dll</c>) are allowed by default and configurable
/// (<see cref="PortcullisConventionKeys.SystemWebAllowedTypes"/>); <c>using System.Web;</c>
/// on its own is therefore not reported — the legacy types used through it are, where they
/// are used.
///
/// <b>One report per reference.</b> <c>System.Web.HttpContext.Current</c> binds three
/// names (<c>Web</c>, <c>HttpContext</c>, and the property); only the outermost name that
/// is still a System.Web namespace or type is reported, at the qualified expression
/// (<c>System.Web.HttpContext</c>). Where the chain continues into a name that does not
/// bind at all (<c>System.Web.Mvc.Controller</c> in a scan that has no System.Web.Mvc), the
/// last part that did bind (<c>System.Web</c>) is reported, so an unresolved legacy name is
/// still caught at its System.Web prefix rather than lost.
///
/// <b>What it deliberately leaves alone:</b> names inside a <c>namespace</c> declaration
/// (declaring types in System.Web is implementing it, not using it), names in
/// documentation comments, and — only where the compilation could not resolve the
/// enclosing class's base type — an unqualified name in expression position. In the latter
/// case the real base class may declare a member of that name (an ASP.NET Core controller's
/// <c>HttpContext</c> property is the textbook case), and the rule cannot see it; reporting
/// would be a false positive on exactly the code a migration produces.
/// </summary>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class SystemWebUsageAnalyzer : DiagnosticAnalyzer
{
    private const string Id = "PORTCULLIS_MIG_SYSTEM_WEB";
    private const string Title = "System.Web API used in code that targets modern .NET";
    private const string MessageFormat =
        "'{0}' is System.Web, which modern .NET does not have: this builds only through a " +
        "compatibility shim or a leftover .NET Framework reference. Use the ASP.NET Core " +
        "equivalent (Microsoft.AspNetCore.*) instead.";

    public static readonly DiagnosticDescriptor SystemWebRule = new(
        Id, Title, MessageFormat, MigrationSymbols.Category, DiagnosticSeverity.Warning, isEnabledByDefault: true);

    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics { get; } =
        ImmutableArray.Create(SystemWebRule);

    public override void Initialize(AnalysisContext context)
    {
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.EnableConcurrentExecution();
        context.RegisterCompilationStartAction(start =>
        {
            var conventions = PortcullisConventions.From(start.Options);
            var isExempt = MigrationSymbols.ExemptionFor(conventions);
            var allowed = conventions.SystemWebAllowedTypes.ToImmutableHashSet(StringComparer.Ordinal);

            start.RegisterSyntaxNodeAction(
                c => AnalyzeUsingDirective(c, isExempt, allowed), SyntaxKind.UsingDirective);
            start.RegisterSyntaxNodeAction(
                c => AnalyzeName(c, isExempt, allowed), SyntaxKind.IdentifierName, SyntaxKind.GenericName);
        });
    }

    // --- using directives: `using System.Web.Mvc;`, `using static …`, `using X = …` ---

    private static void AnalyzeUsingDirective(
        SyntaxNodeAnalysisContext context, Func<SyntaxTree, bool> isExempt, ImmutableHashSet<string> allowed)
    {
        var directive = (UsingDirectiveSyntax)context.Node;
        if (isExempt(directive.SyntaxTree))
        {
            return;
        }

        // The longest leading part of the imported name that binds. In a scan that has
        // System.Web but not System.Web.Mvc, `System.Web.Mvc` binds to nothing while its
        // `System.Web` prefix binds to the System.Web namespace.
        var target = directive.NamespaceOrType;
        TypeSyntax? bound = target;
        var symbol = context.SemanticModel.GetSymbolInfo(bound, context.CancellationToken).Symbol;
        while (symbol == null && bound is QualifiedNameSyntax)
        {
            bound = ((QualifiedNameSyntax)bound).Left;
            symbol = context.SemanticModel.GetSymbolInfo(bound, context.CancellationToken).Symbol;
        }

        if (IsLegacyImport(symbol, bound == target, allowed))
        {
            context.ReportDiagnostic(Diagnostic.Create(SystemWebRule, directive.GetLocation(), target.ToString()));
        }
    }

    private static bool IsLegacyImport(ISymbol? symbol, bool isWholeTarget, ImmutableHashSet<string> allowed)
    {
        // `using System.Web;` imports a namespace modern .NET still has, because an allowed
        // type lives there. Only the whole target gets that exemption: in
        // `using System.Web.Mvc;` a bound `System.Web` prefix is just where the unresolved
        // import is anchored, and the import itself is legacy.
        var ns = symbol as INamespaceSymbol;
        if (ns != null)
        {
            return MigrationSymbols.IsSystemWebNamespace(ns) && !(isWholeTarget && IsHomeOfAnAllowedType(ns, allowed));
        }

        var type = symbol as INamedTypeSymbol;
        return type != null && IsReportableType(type, allowed);
    }

    // Some allowed type is declared directly in this namespace (not in one nested inside it).
    private static bool IsHomeOfAnAllowedType(INamespaceSymbol ns, ImmutableHashSet<string> allowed)
    {
        var prefix = MigrationSymbols.QualifiedPrefix(ns);
        return allowed.Any(type => type.StartsWith(prefix, StringComparison.Ordinal)
            && !type.Substring(prefix.Length).Contains("."));
    }

    private static bool IsReportableType(INamedTypeSymbol type, ImmutableHashSet<string> allowed) =>
        MigrationSymbols.IsSystemWebType(type) && !allowed.Contains(MigrationSymbols.MetadataName(type));

    // --- every other name: type references, qualified names, member-access chains ---

    private static void AnalyzeName(
        SyntaxNodeAnalysisContext context, Func<SyntaxTree, bool> isExempt, ImmutableHashSet<string> allowed)
    {
        var name = (SimpleNameSyntax)context.Node;
        if (IsDeclarationOrDocumentation(name) || isExempt(name.SyntaxTree))
        {
            return;
        }

        var model = context.SemanticModel;
        var symbol = Bind(model, name, context.CancellationToken);
        if (!IsSystemWeb(symbol))
        {
            return;
        }

        var reportNode = QualifiedExpressionFor(name);
        if (IsSystemWeb(Bind(model, NextNameInChain(reportNode), context.CancellationToken)))
        {
            return; // the outer name reports, once, for the whole reference
        }

        var type = symbol as INamedTypeSymbol;
        if (type != null && !IsReportableType(type, allowed))
        {
            return; // an allowed type, e.g. System.Web.HttpUtility
        }

        if (reportNode == name
            && !SyntaxFacts.IsInTypeOnlyContext(name)
            && EnclosingTypeHasUnresolvedBase(model, name, context.CancellationToken))
        {
            return;
        }

        var reported = type != null
            ? MigrationSymbols.MetadataName(type)
            : MigrationSymbols.NamespaceName((INamespaceSymbol)symbol!);
        context.ReportDiagnostic(Diagnostic.Create(SystemWebRule, reportNode.GetLocation(), reported));
    }

    // An attribute name binds to the attribute's constructor, not to its type.
    private static ISymbol? Bind(SemanticModel model, SimpleNameSyntax? name, CancellationToken cancellationToken)
    {
        if (name == null)
        {
            return null;
        }

        var symbol = model.GetSymbolInfo(name, cancellationToken).Symbol;
        var method = symbol as IMethodSymbol;
        return method != null && method.MethodKind == MethodKind.Constructor ? method.ContainingType : symbol;
    }

    private static bool IsSystemWeb(ISymbol? symbol) => symbol switch
    {
        INamespaceSymbol ns => MigrationSymbols.IsSystemWebNamespace(ns),
        INamedTypeSymbol type => MigrationSymbols.IsSystemWebType(type),
        _ => false,
    };

    // `HttpContext` in `System.Web.HttpContext` stands for the qualified expression as a whole.
    private static ExpressionSyntax QualifiedExpressionFor(SimpleNameSyntax name) => name.Parent switch
    {
        QualifiedNameSyntax qualified when qualified.Right == name => qualified,
        MemberAccessExpressionSyntax access when access.Name == name => access,
        _ => name,
    };

    // The name that continues a qualified expression: `HttpContext` after `System.Web`.
    private static SimpleNameSyntax? NextNameInChain(ExpressionSyntax node) => node.Parent switch
    {
        QualifiedNameSyntax qualified when qualified.Left == node => qualified.Right,
        MemberAccessExpressionSyntax access when access.Expression == node => access.Name,
        _ => null,
    };

    private static bool IsDeclarationOrDocumentation(SimpleNameSyntax name)
    {
        if (name.IsPartOfStructuredTrivia())
        {
            return true;
        }

        SyntaxNode top = name;
        while (top.Parent is QualifiedNameSyntax)
        {
            top = top.Parent;
        }

        // Using directives are reported whole by AnalyzeUsingDirective; a namespace
        // declaration's own name is where types are declared, not used.
        return top.Parent is UsingDirectiveSyntax or BaseNamespaceDeclarationSyntax;
    }

    // The class a name sits in, or any class around that, derives from something the
    // compilation could not resolve.
    private static bool EnclosingTypeHasUnresolvedBase(SemanticModel model, SyntaxNode node, CancellationToken cancellationToken)
    {
        for (var symbol = model.GetEnclosingSymbol(node.SpanStart, cancellationToken); symbol != null; symbol = symbol.ContainingSymbol)
        {
            var type = symbol as INamedTypeSymbol;
            if (type != null && HasUnresolvedBase(type))
            {
                return true;
            }
        }

        return false;
    }

    private static bool HasUnresolvedBase(INamedTypeSymbol type)
    {
        for (var baseType = type.BaseType; baseType != null; baseType = baseType.BaseType)
        {
            if (baseType.TypeKind == TypeKind.Error)
            {
                return true;
            }
        }

        return false;
    }
}
