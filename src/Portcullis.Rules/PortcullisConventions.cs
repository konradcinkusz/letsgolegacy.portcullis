using System.Collections.Immutable;
using Microsoft.CodeAnalysis.Diagnostics;

namespace Portcullis.Rules;

/// <summary>
/// The configuration key names both hosts use, defined once here so they cannot drift.
///
/// The analyzer package reads these from an <c>.editorconfig</c>/<c>.globalconfig</c> via
/// Roslyn; the CLI reads an <c>portcullis.json</c> and flattens it onto these same strings
/// before handing them to the same parser. Neither host reimplements the convention model
/// — the JSON reader's only job is to produce this key/value shape.
///
/// The <c>portcullis_</c> prefix (rather than <c>dotnet_</c> or <c>build_property.</c>) is
/// deliberate: <c>.globalconfig</c> keys are a flat global namespace shared with every
/// other analyzer in a consumer's build, and these need to be unmistakably ours. Note
/// that this prefix, like the <c>PORTCULLIS_P*</c> diagnostic ids, becomes an API contract
/// the moment a consumer writes one into their config — it was part of the rename
/// surface, for exactly the same reason.
/// </summary>
public static class PortcullisConventionKeys
{
    public const string KernelFolders = "portcullis_kernel_folders";
    public const string EntityFolders = "portcullis_entity_folders";
    public const string AdapterFolders = "portcullis_adapter_folders";
    public const string VendorNamespaces = "portcullis_vendor_namespaces";
    public const string EntryPointFileNames = "portcullis_entry_point_file_names";
    public const string KernelLineCeiling = "portcullis_kernel_line_ceiling";

    /// <summary>
    /// The directory every convention is matched relative to. Host-supplied rather than
    /// team-supplied: the CLI sets it to the scan root automatically.
    ///
    /// Without it, conventions match against the ABSOLUTE file path, so a directory
    /// anywhere above the scan root can satisfy or defeat a convention by accident — a
    /// checkout living under <c>~/Domain/</c> makes every file in the repository an
    /// "entity" file, and a CI runner whose workspace path happens to contain
    /// <c>Integrations</c> exempts the whole tree from the vendor-adapter rule. Both were
    /// silent.
    ///
    /// Excluded from <see cref="All"/> on purpose: it is not a convention a team declares,
    /// so it must not count as "the team configured something" when
    /// <see cref="ConventionCoverageAnalyzer"/> decides whether to report a vacuous scan.
    /// </summary>
    public const string PathRoot = "portcullis_path_root";

    /// <summary>
    /// Every convention key a team can declare, so a reader can enumerate them without
    /// restating the list. Deliberately excludes <see cref="PathRoot"/>, which is
    /// host-supplied plumbing rather than a declared convention.
    /// </summary>
    public static readonly string[] All =
    [
        KernelFolders, EntityFolders, AdapterFolders,
        VendorNamespaces, EntryPointFileNames, KernelLineCeiling,
    ];
}

/// <summary>
/// The folder names, file names, vendor namespaces and line ceiling the rules match
/// against — the thing that turns "portcullis enforces one specific architecture" into
/// "portcullis enforces the architecture you declared".
///
/// Every value here was a hardcoded <c>private static readonly string[]</c> inside an
/// individual analyzer. A team whose shared kernel is called <c>Common</c>, or whose
/// adapters live in <c>External/</c>, got silence from four of the six rules — not a
/// warning that nothing matched, silence — with <c>.editorconfig</c> severity (which can
/// turn a rule off but cannot tell it where to look) as the only knob. That gap is
/// recorded in README's "Honest limits" and docs/TUTORIAL.md section 7, and it is the one
/// a review of the tool called "the single biggest distance between a working tool and a
/// product someone else can adopt".
///
/// The defaults are exactly the previously-hardcoded lists, so a consumer who configures
/// nothing sees byte-identical behaviour to before. Configuration replaces a list rather
/// than adding to it: a team saying their kernel is <c>Common</c> means <c>Common</c>, not
/// <c>Common</c> plus three names they have never used, and an additive model would make
/// it impossible to *narrow* a convention.
///
/// Reading happens through <see cref="AnalyzerConfigOptions"/> rather than by opening a
/// file, because an analyzer cannot do file I/O — it is loaded by the compiler, may run
/// out-of-process in an IDE, and <c>EnforceExtendedAnalyzerRules</c> (set in this
/// project's csproj) makes RS1035 an error on <c>System.IO</c> use. Roslyn substitutes
/// <see cref="AnalyzerOptions.Empty"/> when a host passes none, so every call site here
/// works unconditionally with no null-guarding at the analyzer end.
/// </summary>
public sealed class PortcullisConventions
{
    /// <summary>
    /// The previously-hardcoded values, verbatim. What a consumer gets with no
    /// configuration at all.
    ///
    /// Built with ImmutableArray.Create rather than collection expressions: the
    /// System.Collections.Immutable that ships with this project's pinned
    /// Microsoft.CodeAnalysis 4.8.0 on netstandard2.0 predates the runtime support
    /// collection expressions need, and the compiler rejects them outright (CS9210, "This
    /// version of 'ImmutableArray&lt;T&gt;' cannot be used with collection expressions").
    /// Elsewhere in this repository `["a", "b"]` is fine — those are plain arrays.
    /// </summary>
    public static PortcullisConventions Default { get; } = new(
        kernelFolders: ImmutableArray.Create("ServiceDefaults", "Kernel", "SharedKernel"),
        entityFolders: ImmutableArray.Create("Domain", "Entities"),
        adapterFolders: ImmutableArray.Create("Infrastructure", "Adapters", "Integrations"),
        vendorNamespaces: ImmutableArray.Create(
            "Anthropic", "OpenAI", "Stripe", "Twilio", "SendGrid", "PayPal",
            "Amazon", "Azure", "Google.Cloud", "Firebase", "MailKit"),
        entryPointFileNames: ImmutableArray.Create("Program.cs"),
        kernelLineCeiling: KernelBoundaryAnalyzer.LocCeiling);

    private PortcullisConventions(
        ImmutableArray<string> kernelFolders,
        ImmutableArray<string> entityFolders,
        ImmutableArray<string> adapterFolders,
        ImmutableArray<string> vendorNamespaces,
        ImmutableArray<string> entryPointFileNames,
        int kernelLineCeiling,
        ImmutableHashSet<string>? explicitlyConfiguredKeys = null,
        string? pathRoot = null)
    {
        ExplicitlyConfiguredKeys = explicitlyConfiguredKeys ?? ImmutableHashSet<string>.Empty;
        PathRoot = pathRoot;
        KernelFolders = kernelFolders;
        EntityFolders = entityFolders;
        AdapterFolders = adapterFolders;
        VendorNamespaces = vendorNamespaces;
        EntryPointFileNames = entryPointFileNames;
        KernelLineCeiling = kernelLineCeiling;
    }

    public ImmutableArray<string> KernelFolders { get; }

    public ImmutableArray<string> EntityFolders { get; }

    public ImmutableArray<string> AdapterFolders { get; }

    public ImmutableArray<string> VendorNamespaces { get; }

    public ImmutableArray<string> EntryPointFileNames { get; }

    public int KernelLineCeiling { get; }

    /// <summary>
    /// The <see cref="PortcullisConventionKeys"/> the host actually supplied a value for.
    ///
    /// Exists so <see cref="ConventionCoverageAnalyzer"/> can tell two very different
    /// situations apart when a convention matches nothing: a team who wrote a name that
    /// matches no folder (almost certainly a typo, or a rename that left the config
    /// behind) versus a team who has configured nothing and whose layout simply does not
    /// use the built-in names. The remedy differs, so the message does too.
    /// </summary>
    public ImmutableHashSet<string> ExplicitlyConfiguredKeys { get; }

    public bool IsExplicitlyConfigured(string key) => ExplicitlyConfiguredKeys.Contains(key);

    /// <summary>The directory conventions are matched relative to, or null when the host supplied none.</summary>
    public string? PathRoot { get; }

    /// <summary>
    /// Strips <see cref="PathRoot"/> from an absolute file path, so a convention matches
    /// only folders inside the scanned tree.
    ///
    /// Returns the path unchanged when no root was supplied or the path lies outside it.
    /// That is the pre-existing behaviour and remains the analyzer package's default,
    /// since the compiler has no notion of a "scan root" — a consumer who wants
    /// project-relative matching there sets <see cref="PortcullisConventionKeys.PathRoot"/>
    /// explicitly. Documented in docs/CONFIGURATION.md rather than left as a surprise.
    /// </summary>
    public string Relative(string? filePath)
    {
        if (filePath is null || filePath.Length == 0)
        {
            return string.Empty;
        }

        if (PathRoot is null || PathRoot.Length == 0)
        {
            return filePath;
        }

        if (filePath.Length <= PathRoot.Length
            || !filePath.StartsWith(PathRoot, StringComparison.OrdinalIgnoreCase))
        {
            return filePath;
        }

        // Only strip on a real separator boundary, so a root of "/repo/app" never
        // half-matches "/repo/application/Foo.cs". Substring rather than a range
        // expression: netstandard2.0 has no System.Index/System.Range (CS0518).
        var next = filePath[PathRoot.Length];
        if (next != '/' && next != '\\')
        {
            return PathRoot[PathRoot.Length - 1] is '/' or '\\'
                ? filePath.Substring(PathRoot.Length)
                : filePath;
        }

        return filePath.Substring(PathRoot.Length + 1);
    }

    /// <summary>
    /// Reads whatever the host supplied, falling back to <see cref="Default"/> per key.
    /// Per-key rather than all-or-nothing so a team can override just the one convention
    /// they name differently without restating the rest.
    /// </summary>
    public static PortcullisConventions From(AnalyzerOptions? options)
    {
        var global = options?.AnalyzerConfigOptionsProvider.GlobalOptions;
        if (global is null)
        {
            return Default;
        }

        var configured = ImmutableHashSet.CreateBuilder<string>(StringComparer.Ordinal);
        foreach (var key in PortcullisConventionKeys.All)
        {
            // A value the parser rejects (see ReadInt) deliberately does NOT count as
            // configured. Otherwise `"kernelLineCeiling": 0` — silently discarded — would
            // still register as "this team configured something", which is exactly the
            // signal ConventionCoverageAnalyzer uses to decide a scan is not vacuous.
            if (global.TryGetValue(key, out var raw) && IsUsable(key, raw))
            {
                configured.Add(key);
            }
        }

        return new PortcullisConventions(
            ReadList(global, PortcullisConventionKeys.KernelFolders, Default.KernelFolders),
            ReadList(global, PortcullisConventionKeys.EntityFolders, Default.EntityFolders),
            ReadList(global, PortcullisConventionKeys.AdapterFolders, Default.AdapterFolders),
            ReadList(global, PortcullisConventionKeys.VendorNamespaces, Default.VendorNamespaces),
            ReadList(global, PortcullisConventionKeys.EntryPointFileNames, Default.EntryPointFileNames),
            ReadInt(global, PortcullisConventionKeys.KernelLineCeiling, Default.KernelLineCeiling),
            configured.ToImmutable(),
            global.TryGetValue(PortcullisConventionKeys.PathRoot, out var root) ? root : null);
    }

    /// <summary>
    /// Lists are comma-separated, with surrounding whitespace trimmed and empty entries
    /// dropped — the shape .editorconfig values conventionally take, and the reason the
    /// JSON side flattens arrays to this form rather than inventing a second encoding.
    ///
    /// A key present but empty (<c>portcullis_kernel_folders =</c>) parses to an empty list
    /// and is honoured as one, NOT silently replaced by the defaults. "This project has no
    /// shared kernel" is a legitimate thing to declare, and it is the only way to switch a
    /// convention off deliberately; treating it as "unset" would make that undeclarable.
    /// A key entirely absent is what falls back.
    /// </summary>
    private static ImmutableArray<string> ReadList(
        AnalyzerConfigOptions options, string key, ImmutableArray<string> fallback)
    {
        if (!options.TryGetValue(key, out var raw) || raw is null)
        {
            return fallback;
        }

        var builder = ImmutableArray.CreateBuilder<string>();
        foreach (var part in raw.Split(','))
        {
            var trimmed = part.Trim();
            if (trimmed.Length > 0)
            {
                builder.Add(trimmed);
            }
        }

        return builder.ToImmutable();
    }

    // Whether a supplied value is one this parser will actually honour. Only the integer
    // key can be rejected; a list value is always usable, including an empty one, which is
    // a deliberate "this project has none".
    private static bool IsUsable(string key, string? raw) =>
        key != PortcullisConventionKeys.KernelLineCeiling
        || (int.TryParse(raw, out var parsed) && parsed > 0);

    // An unparseable or non-positive ceiling falls back rather than throwing: an analyzer
    // that throws is reported to the consumer as AD0001 ("analyzer threw an exception"),
    // which tells them nothing about the typo in their config and takes every other rule
    // in this assembly down with it.
    private static int ReadInt(AnalyzerConfigOptions options, string key, int fallback) =>
        options.TryGetValue(key, out var raw) && int.TryParse(raw, out var parsed) && parsed > 0
            ? parsed
            : fallback;
}
