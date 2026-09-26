using System.Collections.Immutable;
using Portcullis.Rules;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;

namespace Portcullis.Engine.Tests.Rules.Mutants;

/// <summary>
/// Mutation-pass variant of <see cref="AntiCorruptionEdgeAnalyzer"/> (docs/MUTATIONS.md,
/// variant "adapter-segment-includes-application"). The real rule's adapter allowlist is
/// ["Infrastructure", "Adapters", "Integrations"]; this mutant swaps "Integrations" for
/// "Application" — the exact layer the real, current the reference consumer app violation
/// (Anthropic.Models.Messages used directly from Consumer.AgentService.Application) lives
/// in. The mutant therefore exempts precisely the folder this rule exists to catch.
/// </summary>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class AdapterSegmentOverbroadMutant : DiagnosticAnalyzer
{
    private static readonly string[] VendorNamespaceRoots =
    [
        "Anthropic", "OpenAI", "Stripe", "Twilio", "SendGrid", "PayPal",
        "Amazon", "Azure", "Google.Cloud", "Firebase", "MailKit",
    ];

    private static readonly string[] AdapterFolderSegments = ["Infrastructure", "Adapters", "Application"]; // the mutation: should be "Integrations"

    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics { get; } =
        ImmutableArray.Create(AntiCorruptionEdgeAnalyzer.VendorSdkOutsideAdapterRule);

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
            if (HasSegment(tree.FilePath, AdapterFolderSegments))
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

                var namespaceRoot = importedName.Split('.')[0];
                if (!VendorNamespaceRoots.Contains(namespaceRoot, StringComparer.Ordinal))
                {
                    continue;
                }

                context.ReportDiagnostic(Diagnostic.Create(
                    AntiCorruptionEdgeAnalyzer.VendorSdkOutsideAdapterRule,
                    usingDirective.GetLocation(),
                    tree.FilePath,
                    importedName));
            }
        }
    }

    private static bool SegmentMatches(string part, string name) =>
        part.Equals(name, StringComparison.OrdinalIgnoreCase)
        || part.EndsWith("." + name, StringComparison.OrdinalIgnoreCase);

    private static bool HasSegment(string? filePath, string[] segments)
    {
        if (string.IsNullOrEmpty(filePath))
        {
            return false;
        }

        var parts = filePath.Split('/', '\\', StringSplitOptions.RemoveEmptyEntries);
        return parts.Any(p => segments.Any(seg => SegmentMatches(p, seg)));
    }
}
