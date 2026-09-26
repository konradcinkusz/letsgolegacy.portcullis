using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;
using Portcullis.Cli;
using Portcullis.Engine;
using Portcullis.Engine.Findings;
using Portcullis.Engine.Provenance;
using Portcullis.Engine.Sarif;

var jsonOptions = new JsonSerializerOptions
{
    PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    WriteIndented = true,
    DefaultIgnoreCondition = JsonIgnoreCondition.Never,
    Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
};

var options = ScanCommandOptions.Parse(args, out var parseError);
if (options is null)
{
    Console.Error.WriteLine($"portcullis: {parseError}");
    Console.Error.WriteLine(ScanCommandOptions.Usage);
    return 2;
}

// Scanner.ScanAsync returns an empty file list for a path that does not exist, the same
// graceful degradation it applies to an empty directory. That is right for a library and
// wrong for a gate: `portcullis scan /typo` would otherwise report filesScanned 0, a
// passing gate, and exit 0 — a green CI check for a scan that never happened. The
// composite action passes "$GITHUB_WORKSPACE/$PC_PATH" straight through, so a wrong
// `path:` input is exactly how this reaches a consumer.
if (!Directory.Exists(options.Path))
{
    Console.Error.WriteLine($"portcullis: '{options.Path}' is not a directory that exists — nothing was scanned.");
    return 2;
}

// The baseline is read before anything is scanned, so a missing or malformed file stops the
// run with exit 2 before any work is done, instead of after it. For --write-baseline nothing is
// read: the scan is judged against a baseline accepting everything it finds, which is exactly
// what is written below.
Baseline? baseline = null;
if (options.BaselinePath is not null)
{
    if (options.WriteBaseline)
    {
        baseline = Baseline.Everything(options.BaselinePath);
    }
    else
    {
        try
        {
            baseline = BaselineFile.Read(options.BaselinePath);
        }
        catch (BaselineFileException ex)
        {
            Console.Error.WriteLine($"portcullis: {ex.Message}");
            return 2;
        }
    }
}

// Provenance is computed here, at the CLI boundary, and handed to Scanner as data —
// the engine itself stays git-agnostic (docs/SPEC.md section 4). --provenance-repo
// defaults to the scanned path itself; pass it explicitly whenever <path> is a
// subdirectory of the repository (e.g. scanning "<repo>/src" while the git history
// lives at "<repo>"), same as Track B's PR-check workflow does.
ProvenanceReport? provenance = null;
string? provenanceRoot = null;
if (options.ProvenanceRange is not null)
{
    provenanceRoot = options.ProvenanceRepo ?? options.Path;
    provenance = new GitProvenanceProvider(provenanceRoot).GetProvenance(options.ProvenanceRange);
}

var result = await Scanner.ScanAsync(options.Path, provenance, provenanceRoot, baseline);

if (options.WriteBaseline)
{
    try
    {
        var written = BaselineFile.Write(options.BaselinePath!, result.Violations);
        Console.Error.WriteLine(
            $"portcullis: wrote {written} finding(s) to the baseline '{options.BaselinePath}'; this run is judged against it.");
    }
    catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
    {
        Console.Error.WriteLine($"portcullis: could not write the baseline file '{options.BaselinePath}': {ex.Message}");
        return 2;
    }
}

// Both warnings below go to stderr rather than being left to whoever reads the JSON, so
// stdout stays exactly the machine-readable document it has always been.

// A config file that exists but could not be read means the scan silently ran on the
// built-in defaults rather than the conventions the team checked in — the same
// vacuously-green failure as a gate that never matched anything, one step earlier.
if (result.Configuration?.Error is { } configError)
{
    Console.Error.WriteLine($"portcullis: warning: {configError}");
    Console.Error.WriteLine("portcullis: warning: scanning with the built-in default conventions instead.");
}

// A degraded provenance report means the requested diff scope could not be honoured and
// the gate fell back to counting the whole tree — a louder outcome than the operator
// asked for, so it is announced rather than inferred from the JSON.
if (result.Gate?.DegradedReason is { } degradedReason)
{
    Console.Error.WriteLine(
        $"portcullis: warning: could not scope the gate to '{options.ProvenanceRange}' — {degradedReason}");
    Console.Error.WriteLine(
        "portcullis: warning: falling back to gating on the whole scan (Gate.Scope \"all\").");
}

// Entries that matched nothing never block anything, but a baseline nobody prunes stops saying
// anything about the code, so they are counted out loud. Not for --write-baseline, whose file
// has just been rewritten from this very scan.
if (!options.WriteBaseline && result.Baseline is { } usedBaseline && usedBaseline.EntryCount > usedBaseline.AcceptedCount)
{
    Console.Error.WriteLine(
        $"portcullis: note: {usedBaseline.EntryCount - usedBaseline.AcceptedCount} of the baseline's " +
        $"{usedBaseline.EntryCount} entries matched no finding (fixed, or changed enough to be a new finding); " +
        "--write-baseline would drop them.");
}

// The SARIF is filtered by the same changed lines the gate was scoped to. When the gate fell
// back to the whole scan (Gate.Scope "all" with a DegradedReason), so does the SARIF, and the
// log carries the reason as a tool notification. Locations are written relative to the
// repository root, which is --provenance-repo when given: a consumer such as GitHub code
// scanning resolves them against the root of the checkout, not against the scanned path.
if (options.SarifPath is not null)
{
    var changedLines = result.Gate is { Scope: "diff" } && provenance is not null
        ? ChangedLines.From(provenance, provenanceRoot!)
        : null;
    var sourceRoot = Path.GetFullPath(options.ProvenanceRepo ?? options.Path);

    try
    {
        var directory = Path.GetDirectoryName(Path.GetFullPath(options.SarifPath));
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        await File.WriteAllTextAsync(options.SarifPath, SarifReport.Serialize(result, sourceRoot, changedLines));
    }
    catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
    {
        Console.Error.WriteLine($"portcullis: could not write the SARIF file '{options.SarifPath}': {ex.Message}");
        return 2;
    }
}

Console.WriteLine(JsonSerializer.Serialize(result, jsonOptions));

// Scanner always populates Gate now; the ?? fallback only matters if that ever changes.
return (result.Gate?.Blocked ?? result.Summary.ErrorCount > 0) ? 1 : 0;
