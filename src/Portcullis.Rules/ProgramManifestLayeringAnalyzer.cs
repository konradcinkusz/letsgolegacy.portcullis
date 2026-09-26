using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Text;

namespace Portcullis.Rules;

/// <summary>
/// architecture-standards P9: Program.cs is a manifest; controllers and minimal-API
/// endpoints are transport only — bind, authorize, delegate — and wiring lives in
/// extension methods and orchestrators, never inline in the transport layer.
///
/// Two diagnostics, both dependency-direction checks around Controllers/DbContext/
/// entities:
///
/// - <see cref="ControllerNoDbContextRule"/> is the literal rule docs/SPEC.md section 5
///   names for P9: a controller or minimal-API class must not reference a DbContext
///   type directly.
/// - <see cref="OrphanEntityRule"/> is this rule's own extension of the same layering
///   discipline to the fixture's one Roslyn-checkable category
///   (fixtures/consumer-violations.json CONSUMER-003 through CONSUMER-011): an entity
///   modelled in the data layer that no controller ever reaches breaks the same
///   "Program.cs is a manifest" promise from the other direction — the manifest never
///   mentions it. No CONSUMER-0xx entry carries a literal P9 citation (the fixture says
///   so itself); this is the session's own inference, recorded honestly in
///   docs/SPEC.md section 5's closing note rather than presented as a documented
///   mapping.
/// </summary>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class ProgramManifestLayeringAnalyzer : DiagnosticAnalyzer
{
    public static readonly DiagnosticDescriptor ControllerNoDbContextRule = new(
        id: "PORTCULLIS_P9_CONTROLLER_NO_DBCONTEXT",
        title: "Controller references a DbContext directly",
        messageFormat:
            "Controller '{0}' references '{1}' directly. Controllers are transport only — " +
            "bind, authorize, delegate — route through an orchestrator or repository instead " +
            "(architecture-standards P9).",
        category: "P9",
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    public static readonly DiagnosticDescriptor OrphanEntityRule = new(
        id: "PORTCULLIS_P9_ORPHAN_ENTITY",
        title: "EF Core entity is unreachable from any controller",
        messageFormat:
            "'{0}' is exposed via '{1}.{2}' but has no reference anywhere outside its own " +
            "declaring file — an entity the manifest never actually reaches " +
            "(architecture-standards P9).",
        category: "P9",
        defaultSeverity: DiagnosticSeverity.Warning,
        isEnabledByDefault: true);

    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics { get; } =
        ImmutableArray.Create(ControllerNoDbContextRule, OrphanEntityRule);

    public override void Initialize(AnalysisContext context)
    {
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.EnableConcurrentExecution();
        context.RegisterCompilationAction(AnalyzeCompilation);
    }

    private static void AnalyzeCompilation(CompilationAnalysisContext context)
    {
        CheckControllersForDirectDbContext(context);
        CheckOrphanEntities(context);
    }

    // --- PORTCULLIS_P9_CONTROLLER_NO_DBCONTEXT ---

    private static void CheckControllersForDirectDbContext(CompilationAnalysisContext context)
    {
        foreach (var tree in context.Compilation.SyntaxTrees)
        {
            var root = tree.GetRoot(context.CancellationToken);
            foreach (var classDecl in root.DescendantNodes().OfType<ClassDeclarationSyntax>())
            {
                if (!IsController(classDecl))
                {
                    continue;
                }

                var reported = new HashSet<string>(StringComparer.Ordinal);
                foreach (var typeSyntax in classDecl.DescendantNodes().OfType<TypeSyntax>())
                {
                    var name = TypeNameOf(typeSyntax);
                    if (name is null || !name.EndsWith("DbContext", StringComparison.Ordinal))
                    {
                        continue;
                    }

                    if (!reported.Add(name))
                    {
                        continue;
                    }

                    context.ReportDiagnostic(Diagnostic.Create(
                        ControllerNoDbContextRule,
                        typeSyntax.GetLocation(),
                        classDecl.Identifier.Text,
                        name));
                }
            }
        }
    }

    private static bool IsController(ClassDeclarationSyntax classDecl)
    {
        if (classDecl.Identifier.Text.EndsWith("Controller", StringComparison.Ordinal))
        {
            return true;
        }

        if (classDecl.AttributeLists
            .SelectMany(al => al.Attributes)
            .Any(a => AttributeNameOf(a.Name).IndexOf("ApiController", StringComparison.Ordinal) >= 0))
        {
            return true;
        }

        return classDecl.BaseList?.Types
            .Select(t => TypeNameOf(t.Type))
            .Any(n => n is "ControllerBase" or "Controller") == true;
    }

    private static string AttributeNameOf(NameSyntax name) => name switch
    {
        QualifiedNameSyntax q => q.Right.Identifier.Text,
        SimpleNameSyntax s => s.Identifier.Text,
        _ => name.ToString(),
    };

    private static string? TypeNameOf(TypeSyntax type) => type switch
    {
        IdentifierNameSyntax id => id.Identifier.Text,
        GenericNameSyntax g => g.Identifier.Text,
        QualifiedNameSyntax q => TypeNameOf(q.Right),
        _ => null,
    };

    // --- PORTCULLIS_P9_ORPHAN_ENTITY ---

    private static void CheckOrphanEntities(CompilationAnalysisContext context)
    {
        var compilation = context.Compilation;
        var dbSetEntities = new List<(INamedTypeSymbol Entity, PropertyDeclarationSyntax Property, SyntaxTree DeclaringTree)>();

        foreach (var tree in compilation.SyntaxTrees)
        {
            var semanticModel = compilation.GetSemanticModel(tree);
            var root = tree.GetRoot(context.CancellationToken);

            foreach (var classDecl in root.DescendantNodes().OfType<ClassDeclarationSyntax>())
            {
                var isDbContext = classDecl.BaseList?.Types
                    .Select(t => TypeNameOf(t.Type))
                    .Any(n => n == "DbContext") == true;
                if (!isDbContext)
                {
                    continue;
                }

                foreach (var property in classDecl.Members.OfType<PropertyDeclarationSyntax>())
                {
                    if (property.Type is not GenericNameSyntax { Identifier.Text: "DbSet" } generic
                        || generic.TypeArgumentList.Arguments.Count != 1)
                    {
                        continue;
                    }

                    var typeArgument = generic.TypeArgumentList.Arguments[0];
                    if (semanticModel.GetSymbolInfo(typeArgument, context.CancellationToken).Symbol is not INamedTypeSymbol entity)
                    {
                        continue;
                    }

                    if (!entity.Locations.Any(l => l.IsInSource))
                    {
                        continue; // not declared in this compilation; nothing to check
                    }

                    dbSetEntities.Add((entity, property, tree));
                }
            }
        }

        foreach (var (entity, property, dbContextTree) in dbSetEntities)
        {
            if (IsReferencedOutsideDeclaration(context, compilation, entity, property, dbContextTree))
            {
                continue;
            }

            var entityLocation = entity.Locations.First(l => l.IsInSource);
            var dbContextClass = (ClassDeclarationSyntax)property.Parent!;

            context.ReportDiagnostic(Diagnostic.Create(
                OrphanEntityRule,
                entityLocation,
                entity.Name,
                dbContextClass.Identifier.Text,
                property.Identifier.Text));
        }
    }

    private static bool IsReferencedOutsideDeclaration(
        CompilationAnalysisContext context,
        Compilation compilation,
        INamedTypeSymbol entity,
        PropertyDeclarationSyntax dbSetProperty,
        SyntaxTree dbContextTree)
    {
        var entityTree = entity.Locations.First(l => l.IsInSource).SourceTree!;
        var dbSetPropertySpan = dbSetProperty.Span;

        foreach (var tree in compilation.SyntaxTrees)
        {
            if (tree == entityTree)
            {
                continue; // references within the entity's own declaring file don't count
            }

            var semanticModel = compilation.GetSemanticModel(tree);
            var root = tree.GetRoot(context.CancellationToken);

            foreach (var name in root.DescendantNodes().OfType<SimpleNameSyntax>())
            {
                if (!string.Equals(name.Identifier.Text, entity.Name, StringComparison.Ordinal))
                {
                    continue;
                }

                if (tree == dbContextTree && dbSetPropertySpan.Contains(name.Span))
                {
                    continue; // the DbSet<T> declaration itself is not a usage
                }

                var symbol = semanticModel.GetSymbolInfo(name, context.CancellationToken).Symbol;
                if (SymbolEqualityComparer.Default.Equals(symbol, entity))
                {
                    return true;
                }
            }
        }

        return false;
    }
}
