using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;

namespace Portcullis.Rules;

/// <summary>
/// Reports when the folder and file-name conventions the other rules depend on match
/// nothing in the scanned source — the difference between "your architecture is clean"
/// and "nothing was checked".
///
/// Every other analyzer in this assembly is convention-driven, and each one's response to
/// a convention that matches no file is an early return. That is correct behaviour for a
/// rule and terrible behaviour for a gate: a team whose shared kernel is called
/// <c>Common</c> and whose adapters live in <c>External/</c> got a clean, green,
/// completely vacuous scan, with nothing anywhere saying that four of the seven rules had
/// evaluated no files at all. The review that asked for configuration named this as
/// mattering more than the configuration format itself: "a diagnostic when a configured convention matches zero
/// files, so a misconfigured gate is loud instead of vacuously green."
///
/// <b>Two diagnostics, because there are two genuinely different situations, with
/// different confidence and different remedies.</b>
///
/// <see cref="ConventionUnmatchedRule"/> fires per convention, but only for one a team
/// <em>configured themselves</em> and which then matched nothing. That is a high-confidence
/// mistake — a typo, or a folder renamed after the config was written — and it is worth
/// naming precisely.
///
/// <see cref="NoConventionMatchedRule"/> fires at most once, and only when a scan
/// configured nothing at all <em>and</em> matched none of the conventions. That is the
/// vacuous-green case the review describes. It is deliberately all-or-nothing rather than
/// per-convention: most healthy codebases legitimately lack <em>some</em> of these shapes
/// (plenty of good services have no shared kernel), so reporting each miss separately
/// produced four warnings on ordinary code and taught consumers to switch the rule off —
/// the one outcome that guarantees the real signal is never seen either. Only when
/// <em>nothing</em> matched is "the convention-driven rules evaluated nothing here" a
/// statement about the gate rather than a description of the codebase.
///
/// Further decisions worth stating:
///
/// - <b>Warning, not error.</b> Defaulting to error would fail the build of every
///   consumer on first install. A team who wants missing coverage to block can raise it
///   in one .editorconfig line, the same way any other rule here is tuned.
/// - <b>Vendor namespaces are never checked.</b> A repository with no third-party SDK
///   imports is completely ordinary and is not evidence of misconfiguration.
/// - <b>Nothing is reported for an empty compilation.</b> With no source files every
///   convention trivially matches nothing, which says nothing about the configuration.
///
/// The category is <c>Meta</c> rather than a <c>P&lt;n&gt;</c> principle id: "the rules
/// did not evaluate anything" is a statement about the gate, not a violation of any
/// architecture-standards principle, and labelling it as one would misattribute it.
/// </summary>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class ConventionCoverageAnalyzer : DiagnosticAnalyzer
{
    public static readonly DiagnosticDescriptor ConventionUnmatchedRule = new(
        id: "PORTCULLIS_CONVENTION_UNMATCHED",
        title: "A configured architecture convention matched no files",
        messageFormat:
            "'{0}' is configured as {1} but matched no file in this scan, so {2} evaluated " +
            "nothing. Check it for a typo, or for a folder renamed since it was written.",
        category: "Meta",
        defaultSeverity: DiagnosticSeverity.Warning,
        isEnabledByDefault: true);

    public static readonly DiagnosticDescriptor NoConventionMatchedRule = new(
        id: "PORTCULLIS_NO_CONVENTION_MATCHED",
        title: "No architecture convention matched anything, so the convention-driven rules checked nothing",
        messageFormat:
            "No file matched any built-in portcullis convention ({0}), so the convention-driven " +
            "rules evaluated nothing and this scan can pass without having checked them. If " +
            "this codebase uses different names, declare them in an {1} at the scan root.",
        category: "Meta",
        defaultSeverity: DiagnosticSeverity.Warning,
        isEnabledByDefault: true);

    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics { get; } =
        ImmutableArray.Create(ConventionUnmatchedRule, NoConventionMatchedRule);

    public override void Initialize(AnalysisContext context)
    {
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.EnableConcurrentExecution();
        context.RegisterCompilationAction(AnalyzeCompilation);
    }

    private readonly struct Convention(
        string key, string label, ImmutableArray<string> names, bool isFileName, string affectedRules)
    {
        public string Key { get; } = key;

        public string Label { get; } = label;

        public ImmutableArray<string> Names { get; } = names;

        public string AffectedRules { get; } = affectedRules;

        public bool MatchesAny(List<string> paths)
        {
            // An empty list is a deliberate "this project has none" (see
            // PortcullisConventions.ReadList), which is a declaration, not a mistake — so it
            // counts as satisfied rather than as an unmatched convention.
            if (Names.IsEmpty)
            {
                return true;
            }

            foreach (var path in paths)
            {
                if (isFileName
                    ? PathConventions.HasFileName(path, Names)
                    : PathConventions.HasSegment(path, Names))
                {
                    return true;
                }
            }

            return false;
        }
    }

    private static void AnalyzeCompilation(CompilationAnalysisContext context)
    {
        var conventions = PortcullisConventions.From(context.Options);

        // Relative, for the same reason the rules themselves match relative: an absolute
        // path lets a directory above the scan root satisfy a convention by accident, and
        // this analyzer's whole job is to report accurately whether the rules matched.
        var paths = context.Compilation.SyntaxTrees
            .Select(t => conventions.Relative(t.FilePath))
            .Where(p => !string.IsNullOrEmpty(p))
            .ToList();

        if (paths.Count == 0)
        {
            return;
        }
        var checks = new[]
        {
            new Convention(
                PortcullisConventionKeys.KernelFolders, "a shared-kernel folder", conventions.KernelFolders,
                isFileName: false, "PORTCULLIS_P2_KERNEL_LOC_CEILING and PORTCULLIS_P2_KERNEL_ENTITY_REFERENCE"),
            new Convention(
                PortcullisConventionKeys.EntityFolders, "an entity folder", conventions.EntityFolders,
                isFileName: false, "PORTCULLIS_P2_KERNEL_ENTITY_REFERENCE"),
            new Convention(
                PortcullisConventionKeys.AdapterFolders, "an adapter folder", conventions.AdapterFolders,
                isFileName: false, "PORTCULLIS_P11_VENDOR_SDK_OUTSIDE_ADAPTER's adapter exemption"),
            new Convention(
                PortcullisConventionKeys.EntryPointFileNames, "a service entry-point file",
                conventions.EntryPointFileNames,
                isFileName: true, "PORTCULLIS_P15_MISSING_SERVICE_DEFAULTS"),
        };

        var anyMatched = false;
        foreach (var check in checks)
        {
            if (check.MatchesAny(paths))
            {
                anyMatched = true;
                continue;
            }

            if (conventions.IsExplicitlyConfigured(check.Key))
            {
                // Location.None: this is a statement about the whole compilation, not
                // about any one file. Scanner maps a location-less diagnostic to an empty
                // filePath, which CommentFormatter renders under an explicit
                // repository-wide heading rather than an empty one.
                context.ReportDiagnostic(Diagnostic.Create(
                    ConventionUnmatchedRule,
                    Location.None,
                    check.Key,
                    string.Join(", ", check.Names),
                    check.AffectedRules));
            }
        }

        // The vacuous-green case: nothing declared, nothing matched. If the team declared
        // anything at all they are already engaged with the configuration and the
        // per-convention diagnostics above are the useful signal, not this one.
        // Scoped to the keys these checks actually consult, not to every convention key.
        // Guarding on "configured anything at all" meant an unrelated setting — a
        // kernelLineCeiling, say — silenced the vacuous-scan report without the team
        // having declared a single folder name.
        if (!anyMatched && !checks.Any(c => conventions.IsExplicitlyConfigured(c.Key)))
        {
            context.ReportDiagnostic(Diagnostic.Create(
                NoConventionMatchedRule,
                Location.None,
                string.Join("; ", checks.Select(c => $"{c.Label}: {string.Join(", ", c.Names)}")),
                "portcullis.json"));
        }
    }
}
