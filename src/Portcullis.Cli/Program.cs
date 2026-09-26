using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;
using Portcullis.Cli;
using Portcullis.Engine;
using Portcullis.Engine.Provenance;

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

var result = await Scanner.ScanAsync(options.Path, provenance, provenanceRoot);

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

Console.WriteLine(JsonSerializer.Serialize(result, jsonOptions));

// Scanner always populates Gate now; the ?? fallback only matters if that ever changes.
return (result.Gate?.Blocked ?? result.Summary.ErrorCount > 0) ? 1 : 0;
