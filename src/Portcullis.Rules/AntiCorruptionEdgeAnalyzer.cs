using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;

namespace Portcullis.Rules;

/// <summary>
/// architecture-standards P11: anti-corruption at the edge — a vendor SDK's types must
/// not leak past a designated adapter boundary into application/business code. Purely
/// syntactic (a <c>using</c> directive's root namespace against a curated vendor list),
/// like <see cref="KernelBoundaryAnalyzer"/>'s folder-segment convention for "kernel"/
/// "entity" — same reason: nothing in the target source declares an "adapter" role
/// explicitly, and the vendor SDK assembly itself is never referenced by the scanner's
/// single compilation, which takes no NuGet packages (docs/BOOTSTRAP.md's scope note), so
/// there is no symbol to bind against, only the import statement itself.
///
/// <see cref="VendorNamespaceRoots"/> is a deliberately small, explicit starting list, not
/// a general "flag every third-party package" rule — that would drown in false positives
/// against EF Core, ASP.NET Core, or any other legitimate, broadly-referenced dependency.
/// Grounded in a real, current instance rather than a hypothetical one: the
/// <c>Anthropic</c> SDK's <c>Anthropic.Models.Messages</c> types are used directly from
/// the reference consumer app's <c>Consumer.AgentService.Application</c> namespace
/// (<c>AgentTools.cs</c>, <c>AgentOrchestrator.cs</c>) despite that same project already
/// having a dedicated <c>Infrastructure/Claude/</c> folder — confirmed live against a real
/// `portcullis scan`, not assumed.
///
/// <c>Program.cs</c> is a second, filename-based (not folder-based) recognized adapter
/// location, added after that same real scan first ran without it and flagged
/// `Consumer.AgentService/Program.cs` itself for `using Anthropic;` — reading the file
/// showed a completely idiomatic composition-root pattern
/// (<c>builder.Services.AddSingleton(sp =&gt; new AnthropicClient(...))</c>), the textbook
/// "wire the concretion here, hand out the abstraction everywhere else" role a
/// composition root exists to play. That is a genuine miss in the rule's original design,
/// not noise to tune away silently — recorded here, and in docs/MUTATIONS.md's follow-up
/// section, rather than fixed with no trace. The exemption is filename-scoped, not
/// "anything in a service's root folder": a vendor type used elsewhere in Program.cs
/// outside a DI-registration lambda would still be exactly this rule's target.
/// </summary>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class AntiCorruptionEdgeAnalyzer : DiagnosticAnalyzer
{
    public static readonly DiagnosticDescriptor VendorSdkOutsideAdapterRule = new(
        id: "PORTCULLIS_P11_VENDOR_SDK_OUTSIDE_ADAPTER",
        title: "Vendor SDK type referenced outside the adapter boundary",
        messageFormat:
            "'{0}' imports vendor namespace '{1}' directly. Confine third-party SDK types to " +
            "a designated adapter (an Infrastructure/Adapters/Integrations folder) so the " +
            "rest of the codebase depends on your own abstraction, not the vendor's " +
            "(architecture-standards P11).",
        category: "P11",
        defaultSeverity: DiagnosticSeverity.Warning,
        isEnabledByDefault: true);

    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics { get; } =
        ImmutableArray.Create(VendorSdkOutsideAdapterRule);

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
            if (PathConventions.HasSegment(conventions.Relative(tree.FilePath), conventions.AdapterFolders)
                || PathConventions.HasFileName(tree.FilePath, conventions.EntryPointFileNames))
            {
                continue;
            }

            var syntaxRoot = tree.GetRoot(context.CancellationToken);
            foreach (var usingDirective in syntaxRoot.DescendantNodes().OfType<UsingDirectiveSyntax>())
            {
                var importedName = usingDirective.Name?.ToString();
                if (importedName is null)
                {
                    continue;
                }

                if (!MatchesVendorNamespace(importedName, conventions.VendorNamespaces))
                {
                    continue;
                }

                context.ReportDiagnostic(Diagnostic.Create(
                    VendorSdkOutsideAdapterRule,
                    usingDirective.GetLocation(),
                    tree.FilePath,
                    importedName));
            }
        }
    }

    /// <summary>
    /// Matches an import against a configured vendor namespace, on a dotted-segment
    /// boundary: "Azure" matches `using Azure.Storage.Blobs;` and `using Azure;`, and
    /// "Google.Cloud" matches `using Google.Cloud.Storage.V1;` — but neither matches a
    /// namespace that merely starts with the same characters (`AzureFoo`, `Googleplex`).
    ///
    /// The boundary comparison replaces a root-segment-only test — `importedName.Split('.')[0]`
    /// against the list — which silently made every multi-segment entry unreachable. The
    /// shipped list contains exactly one: "Google.Cloud" never matched anything, because
    /// the root of `Google.Cloud.Storage.V1` is "Google". An entry that reads as coverage
    /// while providing none is worse for this product than an admitted gap, so it is
    /// fixed rather than deleted.
    ///
    /// Comparison is case-insensitive. The previous StringComparer.Ordinal meant a
    /// namespace differing only in casing was silently missed, which is the same
    /// looks-covered-isn't failure in a smaller form, and every other name convention in
    /// this assembly already matches case-insensitively.
    /// </summary>
    private static bool MatchesVendorNamespace(string importedName, ImmutableArray<string> vendorNamespaces)
    {
        foreach (var vendor in vendorNamespaces)
        {
            if (importedName.Equals(vendor, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            if (importedName.Length > vendor.Length
                && importedName[vendor.Length] == '.'
                && importedName.StartsWith(vendor, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }
}
