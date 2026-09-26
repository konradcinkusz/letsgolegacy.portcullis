using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Diagnostics;

namespace Portcullis.Engine.Tests.Rules;

/// <summary>
/// Builds the exact same shape of single-compilation, corelib-only compilation that
/// Scanner.cs uses (src/Portcullis.Engine/Scanner.cs), so a rule's unit tests see the same
/// semantic-resolution constraints as a real `portcullis scan` run: no ASP.NET Core, EF
/// Core, or any other framework assembly is referenced, so any name from those
/// frameworks resolves as an unbound/error symbol rather than a bound type — several
/// rules below are written to rely on exactly that (see
/// ExtensibilityInheritanceAnalyzer's own doc comment).
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
            references: [MetadataReference.CreateFromFile(typeof(object).Assembly.Location)],
            options: new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

        var options = configuration is null
            ? null
            : new Portcullis.Engine.Configuration.PortcullisConfigValues(configuration).ToAnalyzerOptions();

        var withAnalyzers = compilation.WithAnalyzers(ImmutableArray.Create(analyzer), options);
        return await withAnalyzers.GetAnalyzerDiagnosticsAsync();
    }
}
