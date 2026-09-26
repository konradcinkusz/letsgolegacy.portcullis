using System.Collections.Immutable;
using Portcullis.Rules;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;

namespace Portcullis.Engine.Tests.Rules.Mutants;

/// <summary>
/// Mutation-pass variant of <see cref="ExtensibilityInheritanceAnalyzer"/>
/// (docs/MUTATIONS.md, variant "custom-base-class-wrong-typekind"). The real rule
/// checks <c>symbol.TypeKind == TypeKind.Class</c>; this mutant checks
/// <c>TypeKind.Struct</c> instead — a base-list entry is never a struct in valid C#, so
/// the condition can never be true and no class-inheritance violation is ever flagged.
/// Reuses the real
/// <see cref="ExtensibilityInheritanceAnalyzer.CustomBaseClassRule"/> descriptor.
/// </summary>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class CustomBaseClassMutant : DiagnosticAnalyzer
{
    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics { get; } =
        ImmutableArray.Create(ExtensibilityInheritanceAnalyzer.CustomBaseClassRule);

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

            if (symbol.TypeKind != TypeKind.Struct // the mutation: should be TypeKind.Class
                || symbol.SpecialType == SpecialType.System_Object
                || !symbol.Locations.Any(l => l.IsInSource))
            {
                continue;
            }

            context.ReportDiagnostic(Diagnostic.Create(
                ExtensibilityInheritanceAnalyzer.CustomBaseClassRule,
                baseType.GetLocation(),
                classDecl.Identifier.Text,
                symbol.Name));
        }
    }
}
