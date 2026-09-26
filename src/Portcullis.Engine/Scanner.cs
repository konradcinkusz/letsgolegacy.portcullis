using System.Diagnostics;
using System.Reflection;
using Portcullis.Engine.Semantics;
using Portcullis.Engine.Configuration;
using Portcullis.Engine.Model;
using Portcullis.Engine.Provenance;
using Portcullis.Rules;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Diagnostics;

namespace Portcullis.Engine;

/// <summary>
/// The Roslyn analyzer harness: parses every *.cs file under a path into one
/// compilation, runs the registered rules against it, and maps diagnostics to
/// the violation record frozen in docs/SPEC.md section 2.
///
/// Scope: syntax + single-compilation semantic analysis only. Target source is parsed
/// directly with CSharpSyntaxTree, not loaded through MSBuildWorkspace/project
/// references — cross-project or NuGet-package semantic checks are out of scope until real
/// project loading exists. Recorded honestly in docs/BOOTSTRAP.md, not hidden here. What
/// the compilation does reference — the runtime's framework assemblies and a declared
/// surface of the legacy .NET Framework APIs the migration rules look for — is
/// <see cref="ScanReferences"/>, and its doc comment says why.
/// </summary>
public static class Scanner
{
    public const string SchemaVersion = "1.0.0";

    private static readonly string[] ExcludedSegments = ["bin", "obj", ".git"];

    /// <summary>
    /// Runs a scan, optionally consuming a <see cref="ProvenanceReport"/> as the
    /// integration-time input docs/SPEC.md section 4 describes: a violation whose
    /// location falls inside an <see cref="ProvenanceSource.Ai"/>-sourced range has its
    /// severity escalated one step (info→warning, warning→error). This is the M4 wiring
    /// of that contract — see docs/M4-INTEGRATION.md section 3 for why it lives here (a
    /// single, testable policy point) rather than duplicated inside each
    /// <see cref="Microsoft.CodeAnalysis.Diagnostics.DiagnosticAnalyzer"/>.
    ///
    /// <paramref name="provenance"/> now does double duty, added in the diff-scoped-gate
    /// milestone that followed M5: the same ranges that drive escalation also scope
    /// <see cref="GateResult"/> — a violation only counts toward
    /// <c>Gate.BlockingErrorCount</c> when its location falls inside <em>any</em> range in
    /// <paramref name="provenance"/>, regardless of that range's <c>Source</c> (escalation
    /// only reads <c>Ai</c> ranges; gating reads all of them, since "was this line part of
    /// the diff at all" doesn't care who wrote it). One flag, two effects: escalation can
    /// only make severity stricter, never more lenient, so reusing it for gating scope
    /// never produces a worse outcome than a hypothetical second, separate flag would —
    /// see docs/DIFF-GATE.md for the full reasoning and verification.
    ///
    /// <paramref name="provenance"/> is caller-supplied rather than computed here: the
    /// engine stays git-agnostic, exactly as docs/SPEC.md section 4 frames provenance as
    /// "an optional input to the rule engine", not something Scanner reaches out and
    /// fetches for itself. <paramref name="provenanceRoot"/> is the absolute directory
    /// <paramref name="provenance"/>'s <c>FilePath</c>s are relative to — which is the
    /// git repository's top-level directory (see <see cref="GitProvenanceProvider"/>),
    /// not necessarily <paramref name="path"/> itself when scanning a subdirectory of a
    /// larger repo. Defaults to <paramref name="path"/> when omitted, for the common case
    /// of scanning a repository's own root.
    ///
    /// A <paramref name="provenance"/> reporting <see cref="ProvenanceStatus.Degraded"/>
    /// is treated as no range at all: the scan falls back to absolute gating
    /// (<c>scope: "all"</c>) and records why in <c>Gate.DegradedReason</c>. It does not
    /// scope to the degraded report's (empty) ranges, which is what previously turned any
    /// git failure into a passing gate — see <see cref="ProvenanceStatus"/> and
    /// docs/DIFF-GATE.md section 3.
    /// </summary>
    public static async Task<ScanResult> ScanAsync(
        string path,
        ProvenanceReport? provenance = null,
        string? provenanceRoot = null,
        CancellationToken cancellationToken = default)
    {
        var fullPath = Path.GetFullPath(path);
        var stopwatch = Stopwatch.StartNew();

        var csFiles = Directory.Exists(fullPath)
            ? Directory.EnumerateFiles(fullPath, "*.cs", SearchOption.AllDirectories)
                .Where(f => !IsExcluded(f, fullPath))
                .OrderBy(f => f, StringComparer.Ordinal)
                .ToList()
            : [];

        var syntaxTrees = new List<SyntaxTree>(csFiles.Count);
        foreach (var file in csFiles)
        {
            var text = await File.ReadAllTextAsync(file, cancellationToken);
            syntaxTrees.Add(CSharpSyntaxTree.ParseText(text, path: file, cancellationToken: cancellationToken));
        }

        var compilation = CSharpCompilation.Create(
            assemblyName: "PortcullisScanTarget",
            syntaxTrees: syntaxTrees,
            references: ScanReferences.All,
            options: new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

        var rules = RuleRegistry.All;
        var violations = new List<Violation>();

        // The conventions the rules match against, read from the scanned repository's own
        // portcullis.json. Passing real AnalyzerOptions here replaces `options: null`, which
        // discarded every form of analyzer configuration — so before this, the CLI,
        // Action and container channel could not be configured at all, while the analyzer
        // package (fed by the compiler) could. Same rules, same code, two behaviours.
        var config = PortcullisConfigFile.Load(fullPath);

        if (rules.Length > 0)
        {
            var withAnalyzers = compilation.WithAnalyzers(rules, config.ToAnalyzerOptions(pathRoot: fullPath));
            var diagnostics = await withAnalyzers.GetAnalyzerDiagnosticsAsync(cancellationToken);
            violations.AddRange(diagnostics
                .Select(d => ToViolation(d, fullPath))
                .OrderBy(v => v.FilePath, StringComparer.Ordinal)
                .ThenBy(v => v.Line));
        }

        GateResult gate;
        if (provenance is { Status: ProvenanceStatus.Degraded })
        {
            // Fail safe, not open. A degraded report has no usable range set, and scoping
            // a gate to an empty one blocks on nothing at all — so a broken checkout used
            // to produce a green gate on a scan full of errors. Falling back to absolute
            // counting is never more lenient than the diff scope it replaces (that scope
            // blocks on 0; this blocks on N >= 0), so the failure mode is now a gate that
            // is too strict and says why, rather than one that is silently vacuous.
            gate = ComputeAbsoluteGate(violations) with { DegradedReason = provenance.DegradedReason };
        }
        else if (provenance is not null)
        {
            var effectiveRoot = provenanceRoot ?? fullPath;
            ApplyProvenanceEscalation(violations, provenance, effectiveRoot, fullPath);
            gate = ComputeDiffScopedGate(violations, provenance, effectiveRoot, fullPath);
        }
        else
        {
            gate = ComputeAbsoluteGate(violations);
        }

        stopwatch.Stop();

        return new ScanResult(
            SchemaVersion,
            EngineVersion,
            fullPath,
            DateTime.UtcNow,
            stopwatch.Elapsed.TotalMilliseconds,
            csFiles.Count,
            rules.Length,
            violations,
            ScanSummary.From(violations),
            gate,
            new ScanConfiguration(config.Path, config.Error));
    }

    private static string EngineVersion =>
        typeof(Scanner).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion
        ?? typeof(Scanner).Assembly.GetName().Version?.ToString()
        ?? "0.0.0";

    private static bool IsExcluded(string filePath, string root)
    {
        var relative = Path.GetRelativePath(root, filePath);
        var segments = relative.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        return segments.Any(s => ExcludedSegments.Contains(s, StringComparer.OrdinalIgnoreCase));
    }

    private static Violation ToViolation(Diagnostic diagnostic, string root)
    {
        var span = diagnostic.Location.GetLineSpan();
        var filePath = span.Path is null ? string.Empty : Path.GetRelativePath(root, span.Path);
        return new Violation(
            diagnostic.Id,
            filePath.Replace(Path.DirectorySeparatorChar, '/'),
            span.StartLinePosition.Line + 1,
            diagnostic.GetMessage(),
            SeverityOf(diagnostic.Severity));
    }

    private static string SeverityOf(DiagnosticSeverity severity) => severity switch
    {
        DiagnosticSeverity.Error => "error",
        DiagnosticSeverity.Warning => "warning",
        DiagnosticSeverity.Info => "info",
        DiagnosticSeverity.Hidden => "info",
        _ => "info",
    };

    // Only ProvenanceSource.Ai escalates — the literal wording of docs/SPEC.md section 4
    // ("apply a stricter defaultSeverity threshold when a violation falls inside an
    // 'ai'-sourced range"). ProvenanceSource.Mixed (an AI co-author alongside a human
    // one, per AiToolClassifier) deliberately does not: a human co-author on the commit
    // is treated the same as review, not as unattended AI output. Recorded here as a
    // policy call, not left implicit.
    private static void ApplyProvenanceEscalation(
        List<Violation> violations, ProvenanceReport provenance, string provenanceRoot, string scanRoot)
    {
        var aiRanges = ResolveRanges(provenance.Ranges.Where(r => r.Source == ProvenanceSource.Ai), provenanceRoot);
        if (aiRanges.Count == 0)
        {
            return;
        }

        for (var i = 0; i < violations.Count; i++)
        {
            var violation = violations[i];
            if (IsWithinRanges(violation, scanRoot, aiRanges))
            {
                violations[i] = violation with { Severity = EscalateSeverity(violation.Severity) };
            }
        }
    }

    // The diff-scoped gate, added after M5's FINDINGS.md reproduced the absolute-gate
    // gap on demand: a violation only blocks when its location falls inside *any*
    // provenance range, regardless of Source — unlike escalation above, which cares only
    // about Ai ranges, "was this line part of the given diff at all" doesn't care who
    // wrote it. Runs after escalation on the same (already-mutated) violations list, so
    // an escalated warning->error correctly counts here too.
    private static GateResult ComputeDiffScopedGate(
        List<Violation> violations, ProvenanceReport provenance, string provenanceRoot, string scanRoot)
    {
        var allRanges = ResolveRanges(provenance.Ranges, provenanceRoot);
        var blockingCount = violations.Count(v => v.Severity == "error" && IsWithinRanges(v, scanRoot, allRanges));
        return new GateResult(blockingCount > 0, "diff", blockingCount);
    }

    // The original, unconditional M0-M5 behavior: any error anywhere in the scan blocks.
    // Still the default whenever no diff range is supplied, for full backward
    // compatibility with every existing caller.
    private static GateResult ComputeAbsoluteGate(IReadOnlyList<Violation> violations)
    {
        var errorCount = violations.Count(v => v.Severity == "error");
        return new GateResult(errorCount > 0, "all", errorCount);
    }

    private static List<(string Path, int StartLine, int EndLine)> ResolveRanges(
        IEnumerable<ProvenanceRange> ranges, string provenanceRoot) =>
        ranges.Select(r => (Path: ToAbsolutePath(provenanceRoot, r.FilePath), r.StartLine, r.EndLine)).ToList();

    private static bool IsWithinRanges(
        Violation violation, string scanRoot, List<(string Path, int StartLine, int EndLine)> ranges)
    {
        var violationPath = ToAbsolutePath(scanRoot, violation.FilePath);
        return ranges.Any(r =>
            string.Equals(r.Path, violationPath, StringComparison.OrdinalIgnoreCase)
            && violation.Line >= r.StartLine
            && violation.Line <= r.EndLine);
    }

    private static string ToAbsolutePath(string root, string relativePath) =>
        Path.GetFullPath(Path.Combine(root, relativePath.Replace('/', Path.DirectorySeparatorChar)));

    private static string EscalateSeverity(string severity) => severity switch
    {
        "info" => "warning",
        "warning" => "error",
        _ => severity,
    };
}
