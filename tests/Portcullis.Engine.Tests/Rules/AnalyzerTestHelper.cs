using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Diagnostics;
using Portcullis.Engine.Semantics;

namespace Portcullis.Engine.Tests.Rules;

/// <summary>
/// Builds the exact same shape of single compilation that Scanner.cs uses
/// (src/Portcullis.Engine/Scanner.cs), against the same references
/// (<see cref="ScanReferences.All"/>: the runtime's framework assemblies plus the declared
/// legacy .NET Framework surface), so a rule's unit tests see the same semantic-resolution
/// constraints as a real `portcullis scan` run: no ASP.NET Core, EF Core, or any NuGet
/// package is referenced, so any name from those resolves as an unbound/error symbol
/// rather than a bound type — several rules are written to rely on exactly that (see
/// ExtensibilityInheritanceAnalyzer's own doc comment) — while System.Web's core types and
/// ConfigurationManager bind, as they do in a scan, for the migration rules.
/// </summary>
internal static class AnalyzerTestHelper
{
    public static Task<ImmutableArray<Diagnostic>> GetDiagnosticsAsync(
        DiagnosticAnalyzer analyzer,
        params (string Path, string Source)[] files)
        => GetDiagnosticsAsync(analyzer, configuration: null, files);

    /// <summary>
    /// The same compilation, with <paramref name="configuration"/> supplied as global
    /// analyzer config — the shape a .globalconfig reaches an analyzer in, and the shape
    /// PortcullisConfigFile flattens portcullis.json into. Lets a rule's convention-driven
    /// behaviour be tested without writing a file to disk.
    /// </summary>
    public static async Task<ImmutableArray<Diagnostic>> GetDiagnosticsAsync(
        DiagnosticAnalyzer analyzer,
        IReadOnlyDictionary<string, string>? configuration,
        params (string Path, string Source)[] files)
    {
        var trees = files
            .Select(f => CSharpSyntaxTree.ParseText(f.Source, path: f.Path))
            .ToList();

        var compilation = CSharpCompilation.Create(
            assemblyName: "PortcullisRuleTest",
            syntaxTrees: trees,
            references: ScanReferences.All,
            options: new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

        var options = configuration is null
            ? null
            : new Portcullis.Engine.Configuration.PortcullisConfigValues(configuration).ToAnalyzerOptions();

        var withAnalyzers = compilation.WithAnalyzers(ImmutableArray.Create(analyzer), options);
        return WithoutAnalyzerFailures(await withAnalyzers.GetAnalyzerDiagnosticsAsync());
    }

    /// <summary>
    /// Roslyn reports an analyzer that threw as an ordinary diagnostic (AD0001) rather than
    /// failing the analysis — so a test that only asserts a rule stayed silent would pass on
    /// a rule that crashed. Every analysis in these tests is required to finish cleanly.
    /// </summary>
    private static ImmutableArray<Diagnostic> WithoutAnalyzerFailures(ImmutableArray<Diagnostic> diagnostics)
    {
        var failure = diagnostics.FirstOrDefault(d => d.Id == "AD0001");
        Assert.True(failure is null, $"An analyzer threw: {failure?.GetMessage()}");
        return diagnostics;
    }

    /// <summary>
    /// The same analysis against corelib alone, without the scan's framework references and
    /// without its declared legacy surface. For the migration rules this is the proof that
    /// they key on a symbol's identity rather than on Portcullis's own declarations: the
    /// test source declares, say, <c>System.Web.HttpContext</c> itself, the way a real
    /// System.Web or a compatibility shim would, and the rule must still recognise it.
    /// </summary>
    public static async Task<ImmutableArray<Diagnostic>> GetDiagnosticsAgainstCoreLibraryOnlyAsync(
        DiagnosticAnalyzer analyzer,
        params (string Path, string Source)[] files)
    {
        var compilation = CSharpCompilation.Create(
            assemblyName: "PortcullisRuleTestCoreLibraryOnly",
            syntaxTrees: files.Select(f => CSharpSyntaxTree.ParseText(f.Source, path: f.Path)),
            references: [MetadataReference.CreateFromFile(typeof(object).Assembly.Location)],
            options: new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

        var withAnalyzers = compilation.WithAnalyzers(ImmutableArray.Create(analyzer));
        return WithoutAnalyzerFailures(await withAnalyzers.GetAnalyzerDiagnosticsAsync());
    }
}
