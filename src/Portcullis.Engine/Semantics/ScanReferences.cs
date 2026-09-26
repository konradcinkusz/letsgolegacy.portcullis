using System.Collections.Immutable;
using System.Runtime.InteropServices;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace Portcullis.Engine.Semantics;

/// <summary>
/// What a scan's single compilation references: the running .NET runtime's own framework
/// assemblies, plus <see cref="LegacyApiSurface"/> — declarations of the .NET Framework
/// APIs the migration rules look for.
///
/// Why both, rather than <c>System.Private.CoreLib</c> alone as before: the scan parses
/// source directly, without MSBuild, so nothing a project references is present. The
/// convention-driven rules were written for that and do not need more. The migration
/// rules are semantic — they decide on the symbol a name binds to (see
/// <c>Portcullis.Rules.MigrationSymbols</c>) — and against corelib alone the names they
/// exist for bind to nothing at all: <c>httpClient.GetStringAsync(url).Result</c> needs
/// <c>System.Net.Http</c> to know it is a <c>Task&lt;string&gt;</c>, and
/// <c>HttpContext.Current</c> or <c>ConfigurationManager.AppSettings</c> need a declaration
/// of a .NET Framework type that no modern .NET runtime ships. Without both, the CLI, the
/// Action and the container would report nothing for those rules while the analyzer
/// package, fed by a real build, reported everything — the same rules, silent in the
/// channel the pull-request gate runs in.
///
/// Neither addition changes what the other rules see: they only ever act on types declared
/// in the scanned source, and a framework type has no source location.
///
/// Still not referenced, deliberately: NuGet packages and other projects. Resolving those
/// needs the scanned repository's build (restore, project graph), which is the
/// single-compilation limit the README records.
/// </summary>
internal static class ScanReferences
{
    private static readonly Lazy<ImmutableArray<MetadataReference>> FrameworkReferences = new(LoadFramework);

    private static readonly Lazy<ImmutableArray<MetadataReference>> AllReferences =
        new(() => FrameworkReferences.Value.Add(CompileLegacySurface(FrameworkReferences.Value)));

    /// <summary>
    /// Every reference a scan compiles against. Created once per process and shared, so
    /// Roslyn reuses the loaded metadata across compilations instead of re-reading ~170
    /// assemblies for every scan (or every rule test).
    /// </summary>
    public static ImmutableArray<MetadataReference> All => AllReferences.Value;

    /// <summary>
    /// The framework assemblies of the runtime this process runs on: the trusted platform
    /// assemblies that live in the same directory as <c>System.Private.CoreLib</c>. The
    /// host's list also names the application's own assemblies (Roslyn, Portcullis itself);
    /// those are left out, so a scanned repository never binds to Portcullis's own
    /// dependencies.
    /// </summary>
    private static ImmutableArray<MetadataReference> LoadFramework()
    {
        var coreLibrary = typeof(object).Assembly.Location;
        var runtimeDirectory = Path.GetDirectoryName(coreLibrary);
        var trustedPlatformAssemblies = AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES") as string;

        var paths = trustedPlatformAssemblies is null || runtimeDirectory is null
            ? [coreLibrary]
            : trustedPlatformAssemblies
                .Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries)
                .Where(path => string.Equals(
                    Path.GetDirectoryName(path), runtimeDirectory, PathComparison))
                .ToList();

        return paths
            .OrderBy(path => path, StringComparer.Ordinal)
            .Select(path => (MetadataReference)MetadataReference.CreateFromFile(path))
            .ToImmutableArray();
    }

    private static StringComparison PathComparison =>
        RuntimeInformation.IsOSPlatform(OSPlatform.Linux) ? StringComparison.Ordinal : StringComparison.OrdinalIgnoreCase;

    /// <summary>
    /// Compiles <see cref="LegacyApiSurface.Source"/> into an in-memory reference assembly.
    /// A failure here is a defect in that source, not in anything scanned, so it throws
    /// rather than degrading: a legacy surface that silently failed to build would switch
    /// the migration rules off for every scan without a word.
    /// </summary>
    private static MetadataReference CompileLegacySurface(ImmutableArray<MetadataReference> framework)
    {
        var compilation = CSharpCompilation.Create(
            assemblyName: LegacyApiSurface.AssemblyName,
            syntaxTrees: [CSharpSyntaxTree.ParseText(LegacyApiSurface.Source, path: "LegacyApiSurface.cs")],
            references: framework,
            options: new CSharpCompilationOptions(
                OutputKind.DynamicallyLinkedLibrary,
                nullableContextOptions: NullableContextOptions.Disable,
                deterministic: true));

        using var image = new MemoryStream();
        var emitted = compilation.Emit(image);
        if (!emitted.Success)
        {
            var errors = emitted.Diagnostics
                .Where(d => d.Severity == DiagnosticSeverity.Error)
                .Select(d => d.ToString());
            throw new InvalidOperationException(
                "The legacy API surface failed to compile: " + string.Join("; ", errors));
        }

        return MetadataReference.CreateFromImage(image.ToArray());
    }
}
