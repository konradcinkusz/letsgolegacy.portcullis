using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;

namespace Portcullis.Rules;

/// <summary>
/// architecture-standards P4: persistence is provider-portable and migrated. Two
/// mechanical checks, exactly as docs/SPEC.md section 5's P4 entry describes: a
/// <c>Database.EnsureCreated()</c>/<c>EnsureCreatedAsync()</c> call with no enclosing
/// <c>IsInMemory()</c> guard, and a <c>HasData(...)</c> seed call inside
/// <c>OnModelCreating</c>.
///
/// Both checks are purely syntactic — like every other rule in this milestone's
/// single-compilation, corelib-only scanner (docs/BOOTSTRAP.md's scope note), the real EF
/// Core <c>DatabaseFacade</c>/<c>ModelBuilder</c> types never resolve to a bound symbol
/// here, so this rule matches on invocation shape (a member access named "Database"
/// followed by "EnsureCreated"/"EnsureCreatedAsync"; a method named "OnModelCreating"
/// containing a "HasData" call) — the same name-matching technique
/// <see cref="ProgramManifestLayeringAnalyzer"/> already uses for "DbContext"/"Controller".
///
/// The guard shape this rule recognizes (<c>if (x.Database.IsInMemory()) { ...
/// EnsureCreatedAsync ... }</c>) is not a hypothetical — it is exactly what
/// the reference consumer app's own `Consumer.ServiceDefaults/DatabaseExtensions.cs` does today, with a
/// doc comment naming this very principle: "Always MigrateAsync for a real provider —
/// never EnsureCreated." That file is this rule's true-negative case, verified live
/// against a real `portcullis scan`, not just a synthetic fixture.
/// </summary>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class PersistencePortabilityAnalyzer : DiagnosticAnalyzer
{
    public static readonly DiagnosticDescriptor EnsureCreatedOutsideTestRule = new(
        id: "PORTCULLIS_P4_ENSURE_CREATED_OUTSIDE_TEST",
        title: "Database.EnsureCreated() used without an InMemory guard",
        messageFormat:
            "'{0}' is called with no enclosing 'IsInMemory()' guard. EnsureCreated does not " +
            "record a migration, so the schema freezes at first-boot state while migrations " +
            "accumulate in code — use Migrate/MigrateAsync for a real provider " +
            "(architecture-standards P4).",
        category: "P4",
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    public static readonly DiagnosticDescriptor SeedDataInModelRule = new(
        id: "PORTCULLIS_P4_SEED_DATA_IN_MODEL",
        title: "Seed data supplied via HasData inside OnModelCreating",
        messageFormat:
            "'{0}.OnModelCreating' seeds data via HasData. Baking seed rows into the model " +
            "snapshot couples them to migrations and is provider-fragile — seed by a " +
            "versioned script or hosted service instead (architecture-standards P4).",
        category: "P4",
        defaultSeverity: DiagnosticSeverity.Warning,
        isEnabledByDefault: true);

    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics { get; } =
        ImmutableArray.Create(EnsureCreatedOutsideTestRule, SeedDataInModelRule);

    public override void Initialize(AnalysisContext context)
    {
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.EnableConcurrentExecution();
        context.RegisterCompilationAction(AnalyzeCompilation);
    }

    private static void AnalyzeCompilation(CompilationAnalysisContext context)
    {
        foreach (var tree in context.Compilation.SyntaxTrees)
        {
            var root = tree.GetRoot(context.CancellationToken);
            foreach (var invocation in root.DescendantNodes().OfType<InvocationExpressionSyntax>())
            {
                CheckEnsureCreated(context, invocation);
                CheckSeedDataInModel(context, invocation);
            }
        }
    }

    // --- PORTCULLIS_P4_ENSURE_CREATED_OUTSIDE_TEST ---

    private static void CheckEnsureCreated(CompilationAnalysisContext context, InvocationExpressionSyntax invocation)
    {
        if (invocation.Expression is not MemberAccessExpressionSyntax
            {
                Name.Identifier.Text: "EnsureCreated" or "EnsureCreatedAsync",
                Expression: MemberAccessExpressionSyntax { Name.Identifier.Text: "Database" },
            } memberAccess)
        {
            return;
        }

        if (IsGuardedByIsInMemoryCheck(invocation))
        {
            return;
        }

        context.ReportDiagnostic(Diagnostic.Create(
            EnsureCreatedOutsideTestRule,
            invocation.GetLocation(),
            memberAccess.Name.Identifier.Text));
    }

    // Matches the one real, legitimate shape this session found in the reference consumer app:
    // `if (context.Database.IsInMemory()) { await context.Database.EnsureCreatedAsync(...); }`.
    // Deliberately structural, not exhaustive — an early-return-guard style
    // (`if (!IsInMemory()) return;`) is not recognized in this first cut.
    private static bool IsGuardedByIsInMemoryCheck(SyntaxNode node) =>
        node.Ancestors()
            .OfType<IfStatementSyntax>()
            .Any(ifStatement => ifStatement.Condition
                .DescendantNodesAndSelf()
                .OfType<InvocationExpressionSyntax>()
                .Any(inv => MethodNameOf(inv.Expression) == "IsInMemory"));

    // --- PORTCULLIS_P4_SEED_DATA_IN_MODEL ---

    private static void CheckSeedDataInModel(CompilationAnalysisContext context, InvocationExpressionSyntax invocation)
    {
        if (MethodNameOf(invocation.Expression) != "HasData")
        {
            return;
        }

        var containingMethod = invocation.Ancestors().OfType<MethodDeclarationSyntax>().FirstOrDefault();
        if (containingMethod?.Identifier.Text != "OnModelCreating")
        {
            return;
        }

        var containingClass = containingMethod.Ancestors().OfType<ClassDeclarationSyntax>().FirstOrDefault();

        context.ReportDiagnostic(Diagnostic.Create(
            SeedDataInModelRule,
            invocation.GetLocation(),
            containingClass?.Identifier.Text ?? "<unknown>"));
    }

    private static string? MethodNameOf(ExpressionSyntax expression) => expression switch
    {
        MemberAccessExpressionSyntax m => m.Name.Identifier.Text,
        IdentifierNameSyntax id => id.Identifier.Text,
        _ => null,
    };
}
