using System.Collections.Concurrent;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Operations;

namespace Portcullis.Rules;

/// <summary>
/// Symbol identity shared by the four migration rules (<c>PORTCULLIS_MIG_*</c>), which
/// look for .NET Framework idioms that survive a move to modern .NET — see
/// docs/rules/MIGRATION.md.
///
/// Every decision here is made on a symbol the compiler bound, never on the text of the
/// source. A type counts as System.Web because the namespace it is declared in <em>is</em>
/// System.Web, not because "System.Web" appears somewhere in the file. That is the whole
/// point of doing this semantically: it keeps <c>Microsoft.AspNetCore.Http.HttpContext</c>,
/// a team's own <c>HttpContext</c> class and <c>Microsoft.Extensions.Configuration
/// .ConfigurationManager</c> (the modern one, which `WebApplicationBuilder.Configuration`
/// returns) out of these rules, and it still catches the idioms when they arrive through
/// an alias (<c>using Ctx = System.Web.HttpContext;</c>), a <c>using static</c> import or
/// a fully-qualified name that a text search for the short name would have missed.
///
/// Types are compared by their fully-qualified metadata name rather than against
/// <c>Compilation.GetTypeByMetadataName</c>, because that API returns null as soon as two
/// referenced assemblies declare the same name — which is exactly the situation in a
/// half-migrated solution that references both the real System.Web and a compatibility
/// shim — and a rule that goes silent then is a rule that goes silent when it matters.
///
/// Written without pattern variables in conditions, here and in the four rules, on
/// purpose: Stryker.NET (docs/MUTATIONS.md section 8) negates conditions, and a negated
/// <c>x is T t</c> leaves <c>t</c> unassigned, which fails the build of that mutant and
/// makes Stryker drop every other mutation in the method. Plain casts keep all of them
/// testable.
/// </summary>
internal static class MigrationSymbols
{
    /// <summary>The <c>DiagnosticDescriptor.Category</c> every migration rule carries.</summary>
    public const string Category = "Migration";

    /// <summary>
    /// A namespace's dotted name with a trailing dot — <c>"System.Web."</c> — or an empty
    /// string for the global namespace, so a type's full name is prefix plus name and
    /// "this namespace or one nested in it" is a single prefix test.
    /// </summary>
    public static string QualifiedPrefix(INamespaceSymbol ns) =>
        ns.IsGlobalNamespace ? string.Empty : QualifiedPrefix(ns.ContainingNamespace) + ns.Name + ".";

    /// <summary>The dotted name of a namespace (<c>System.Web.Mvc</c>), for messages.</summary>
    public static string NamespaceName(INamespaceSymbol ns) => QualifiedPrefix(ns).TrimEnd('.');

    /// <summary>True for <c>System.Web</c> itself and for every namespace nested in it.</summary>
    public static bool IsSystemWebNamespace(INamespaceSymbol ns) =>
        QualifiedPrefix(ns).StartsWith("System.Web.", StringComparison.Ordinal);

    /// <summary>
    /// True when a type is declared in a System.Web namespace. Roslyn answers a nested
    /// type's <c>ContainingNamespace</c> with the namespace of its outermost container, so
    /// nested types need no special case.
    /// </summary>
    public static bool IsSystemWebType(INamedTypeSymbol type) => IsSystemWebNamespace(type.ContainingNamespace);

    /// <summary>
    /// The CLR metadata name of a type's definition: namespace, containing types joined by
    /// <c>+</c>, and the generic arity suffix — <c>System.Threading.Tasks.Task`1</c>,
    /// <c>System.Runtime.CompilerServices.ConfiguredTaskAwaitable`1+ConfiguredTaskAwaiter</c>.
    /// Stable across generic construction, so <c>Task&lt;int&gt;</c> and
    /// <c>Task&lt;string&gt;</c> both answer <c>Task`1</c>.
    /// </summary>
    public static string MetadataName(INamedTypeSymbol type)
    {
        var definition = type.OriginalDefinition;
        var name = definition.MetadataName;
        for (var containing = definition.ContainingType; containing != null; containing = containing.ContainingType)
        {
            name = containing.MetadataName + "+" + name;
        }

        return QualifiedPrefix(definition.ContainingNamespace) + name;
    }

    /// <summary>
    /// True when an operation sits inside <c>nameof(...)</c>. The compiler still binds the
    /// argument of a <c>nameof</c> — so it reaches an operation action as an ordinary member
    /// reference — but nothing is read or called there; only the name is taken.
    /// </summary>
    public static bool IsInsideNameOf(IOperation operation)
    {
        for (var current = operation.Parent; current != null; current = current.Parent)
        {
            if (current.Kind == OperationKind.NameOf)
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// The per-compilation test for <see cref="PortcullisConventions.MigrationExemptFolders"/>,
    /// cached per syntax tree because the operation and syntax-node actions that consult it
    /// run once per member access, not once per file.
    /// </summary>
    public static Func<SyntaxTree, bool> ExemptionFor(PortcullisConventions conventions)
    {
        var cache = new ConcurrentDictionary<SyntaxTree, bool>();
        return tree => cache.GetOrAdd(
            tree,
            t => PathConventions.HasSegment(conventions.Relative(t.FilePath), conventions.MigrationExemptFolders));
    }
}
