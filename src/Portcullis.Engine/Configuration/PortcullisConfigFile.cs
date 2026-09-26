using System.Collections.Immutable;
using System.Text.Json;
using System.Text.Json.Serialization;
using Portcullis.Rules;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;

namespace Portcullis.Engine.Configuration;

/// <summary>
/// Reads an <c>portcullis.json</c> from a scanned repository and turns it into the same
/// key/value pairs the analyzer package receives from a <c>.globalconfig</c>.
///
/// Two hosts, one convention model. An analyzer cannot open a file — it is loaded by the
/// compiler, may run out-of-process in an IDE, and this project sets
/// <c>EnforceExtendedAnalyzerRules</c>, which makes <c>System.IO</c> use an error
/// (RS1035) — so the analyzer channel has to be fed through Roslyn's own
/// <see cref="AnalyzerConfigOptions"/>. The CLI has no such limit and JSON is a far
/// better fit for a checked-in project file than INI-with-commas. Rather than let that
/// produce two parsers that drift, this class does the narrow job of *reading the file*
/// and hands the result to <see cref="PortcullisConventions"/>, which is the single place
/// that knows what a convention is and what the defaults are.
///
/// Deliberately not a schema-validating reader: an unknown property is ignored rather
/// than rejected, so a config written for a newer portcullis still works with an older one.
/// A malformed file is a different matter and is reported — see <see cref="Load"/>.
/// </summary>
public sealed class PortcullisConfigFile
{
    public const string FileName = "portcullis.json";

    private static readonly JsonSerializerOptions ReadOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
    };

    private PortcullisConfigFile(ImmutableDictionary<string, string> values, string? path, string? error)
    {
        Values = values;
        Path = path;
        Error = error;
    }

    /// <summary>The flattened <c>portcullis_*</c> key/value pairs, ready to hand to Roslyn.</summary>
    public ImmutableDictionary<string, string> Values { get; }

    /// <summary>The file actually read, or null when no config file was present.</summary>
    public string? Path { get; }

    /// <summary>
    /// Set when a config file exists but could not be read. The scan continues on
    /// defaults — a broken config must not take the gate down — but the caller is
    /// expected to surface this, because silently ignoring a config a team believes is in
    /// force is the same vacuously-green failure the convention-coverage diagnostic
    /// exists to prevent, arriving one step earlier.
    /// </summary>
    public string? Error { get; }

    public static PortcullisConfigFile None { get; } =
        new(ImmutableDictionary<string, string>.Empty, null, null);

    /// <summary>
    /// Loads <c>portcullis.json</c> from <paramref name="directory"/> if it exists.
    /// Not a recursive search up the tree: an implicit parent-directory config is exactly
    /// the kind of action-at-a-distance that makes "why is this rule not firing" hard to
    /// answer, and the scan root is always explicit on the command line.
    /// </summary>
    public static PortcullisConfigFile Load(string directory)
    {
        var path = System.IO.Path.Combine(directory, FileName);
        if (!File.Exists(path))
        {
            return None;
        }

        PortcullisConfigDocument? document;
        try
        {
            document = JsonSerializer.Deserialize<PortcullisConfigDocument>(File.ReadAllText(path), ReadOptions);
        }
        catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException)
        {
            return new PortcullisConfigFile(
                ImmutableDictionary<string, string>.Empty, path, $"{path} could not be read: {ex.Message}");
        }

        if (document is null)
        {
            return new PortcullisConfigFile(
                ImmutableDictionary<string, string>.Empty, path, $"{path} is empty or contains only 'null'.");
        }

        var builder = ImmutableDictionary.CreateBuilder<string, string>(StringComparer.OrdinalIgnoreCase);
        AddList(builder, PortcullisConventionKeys.KernelFolders, document.KernelFolders);
        AddList(builder, PortcullisConventionKeys.EntityFolders, document.EntityFolders);
        AddList(builder, PortcullisConventionKeys.AdapterFolders, document.AdapterFolders);
        AddList(builder, PortcullisConventionKeys.VendorNamespaces, document.VendorNamespaces);
        AddList(builder, PortcullisConventionKeys.EntryPointFileNames, document.EntryPointFileNames);
        if (document.KernelLineCeiling is { } ceiling)
        {
            builder[PortcullisConventionKeys.KernelLineCeiling] = ceiling.ToString();
        }

        return new PortcullisConfigFile(builder.ToImmutable(), path, null);
    }

    // A JSON array becomes the comma-separated form .editorconfig uses, so both hosts
    // reach PortcullisConventions.ReadList through the identical string shape rather than
    // through two separately-maintained parsers. A property absent from the JSON adds no
    // key at all, which is what makes PortcullisConventions fall back per-key; a property
    // present but empty ([]) adds an empty value, which it honours as a deliberate "this
    // project has none".
    private static void AddList(
        ImmutableDictionary<string, string>.Builder builder, string key, IReadOnlyList<string>? values)
    {
        if (values is null)
        {
            return;
        }

        builder[key] = string.Join(",", values);
    }

    /// <summary>
    /// Builds the <see cref="AnalyzerOptions"/> a <c>CompilationWithAnalyzers</c> needs so
    /// the rules see this configuration. Returns options carrying no additional files —
    /// the conventions travel entirely as global config values.
    /// </summary>
    public AnalyzerOptions ToAnalyzerOptions() => ToAnalyzerOptions(pathRoot: null);

    /// <summary>
    /// The same options, with the directory conventions should be matched relative to.
    /// The CLI always supplies the scan root: syntax trees carry absolute paths, so
    /// without it a checkout living under (say) <c>~/Domain/</c> would make every file in
    /// the repository match the entity convention.
    /// </summary>
    public AnalyzerOptions ToAnalyzerOptions(string? pathRoot)
    {
        var values = pathRoot is null or ""
            ? Values
            : Values.SetItem(PortcullisConventionKeys.PathRoot, pathRoot);

        return new AnalyzerOptions([], new DictionaryAnalyzerConfigOptionsProvider(values));
    }
}

/// <summary>
/// Convention values supplied directly rather than read from a file, wrapped in the same
/// Roslyn plumbing <see cref="PortcullisConfigFile"/> uses.
///
/// Exists so a host that already has the values — a test, or any future caller that gets
/// configuration from somewhere other than <c>portcullis.json</c> — reaches the analyzers
/// through exactly the same path as the file reader, rather than through a second one
/// that could behave differently.
/// </summary>
public sealed class PortcullisConfigValues(IReadOnlyDictionary<string, string> values)
{
    public AnalyzerOptions ToAnalyzerOptions() =>
        new([], new DictionaryAnalyzerConfigOptionsProvider(
            values.ToImmutableDictionary(kv => kv.Key, kv => kv.Value, StringComparer.OrdinalIgnoreCase)));
}

/// <summary>
/// The on-disk shape of <c>portcullis.json</c>. Property names map to the
/// <see cref="PortcullisConventionKeys"/> constants; the JSON uses ordinary camelCase
/// (<c>kernelFolders</c>) rather than the flat <c>portcullis_kernel_folders</c> key,
/// because the file is unambiguously portcullis's own and does not need the prefix that
/// exists to disambiguate a shared .globalconfig namespace.
/// </summary>
internal sealed record PortcullisConfigDocument
{
    [JsonPropertyName("kernelFolders")]
    public IReadOnlyList<string>? KernelFolders { get; init; }

    [JsonPropertyName("entityFolders")]
    public IReadOnlyList<string>? EntityFolders { get; init; }

    [JsonPropertyName("adapterFolders")]
    public IReadOnlyList<string>? AdapterFolders { get; init; }

    [JsonPropertyName("vendorNamespaces")]
    public IReadOnlyList<string>? VendorNamespaces { get; init; }

    [JsonPropertyName("entryPointFileNames")]
    public IReadOnlyList<string>? EntryPointFileNames { get; init; }

    [JsonPropertyName("kernelLineCeiling")]
    public int? KernelLineCeiling { get; init; }
}

/// <summary>
/// The minimum Roslyn plumbing needed to hand a plain dictionary to an analyzer as global
/// config. Roslyn offers no public in-memory implementation of these two abstractions —
/// the compiler builds its own from real .editorconfig files — so a host that wants to
/// supply options programmatically has to provide one.
///
/// Per-tree and per-additional-file options are deliberately empty: every portcullis
/// convention is repository-wide, and returning global options for a specific tree would
/// let a rule read a value that has no per-file meaning.
/// </summary>
internal sealed class DictionaryAnalyzerConfigOptionsProvider(ImmutableDictionary<string, string> values)
    : AnalyzerConfigOptionsProvider
{
    public override AnalyzerConfigOptions GlobalOptions { get; } = new DictionaryAnalyzerConfigOptions(values);

    public override AnalyzerConfigOptions GetOptions(SyntaxTree tree) => DictionaryAnalyzerConfigOptions.Empty;

    public override AnalyzerConfigOptions GetOptions(AdditionalText textFile) => DictionaryAnalyzerConfigOptions.Empty;
}

internal sealed class DictionaryAnalyzerConfigOptions(ImmutableDictionary<string, string> values)
    : AnalyzerConfigOptions
{
    public static DictionaryAnalyzerConfigOptions Empty { get; } =
        new(ImmutableDictionary<string, string>.Empty);

    public override bool TryGetValue(string key, out string value) => values.TryGetValue(key, out value!);
}
