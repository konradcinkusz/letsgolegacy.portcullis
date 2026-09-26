namespace Portcullis.Cli;

/// <summary>
/// Parsed command-line arguments for `portcullis scan`.
///
/// Extracted out of Program.cs's top-level statements, and made strict, because the loop
/// it replaces silently mis-parsed in two ways that both weaken the merge gate without
/// saying anything. It ran `for (var i = 2; i &lt; args.Length - 1; i++)`, so the final
/// argv element was never examined as a flag name: a trailing `--provenance-range` (the
/// value having been dropped by a shell quoting mistake, say) simply vanished. And with
/// no `default:` arm, a misspelled `--provenence-range` vanished too. In both cases the
/// scan then ran with no provenance at all, which silently changes Gate.Scope from "diff"
/// to "all" — a consumer gets a gate that is not the one their workflow asked for, with
/// no diagnostic anywhere. That is the same class of defect as the fail-open provenance
/// gate <see cref="Portcullis.Engine.Provenance.ProvenanceStatus"/> exists to close, and it
/// would silently defeat that fix, so it is fixed alongside it.
///
/// Shape deliberately mirrors Portcullis.CiComment's <c>CliOptions.Parse</c>, which was
/// already strict and already unit-tested — the correct pattern existed in this
/// repository, it just was not used here. The one addition is an out error message:
/// "unrecognised flag" and "flag is missing its value" are different mistakes and a
/// consumer fixing a workflow deserves to be told which one they made.
/// </summary>
public sealed record ScanCommandOptions(string Path, string? ProvenanceRange, string? ProvenanceRepo)
{
    public const string Usage =
        "usage: portcullis scan <path> [--provenance-range <commitOrRange>] [--provenance-repo <path>]\n" +
        "  --provenance-range now also scopes the merge gate to that range's changed lines\n" +
        "  (Gate.Scope \"diff\") instead of the whole scan (Gate.Scope \"all\", the default).\n" +
        "  If git cannot resolve the range, the gate falls back to \"all\" and says why —\n" +
        "  it does not silently pass.";

    /// <summary>
    /// Parses argv, or returns null with <paramref name="error"/> describing the first
    /// problem found. Pure: does no I/O and never touches the filesystem, so every branch
    /// is unit-testable without a scratch directory. Whether <see cref="Path"/> actually
    /// exists is checked by the caller, where the I/O already lives.
    /// </summary>
    public static ScanCommandOptions? Parse(string[] args, out string error)
    {
        error = string.Empty;

        if (args.Length == 0 || args[0] != "scan")
        {
            error = "the only supported command is 'scan'.";
            return null;
        }

        if (args.Length < 2)
        {
            error = "'scan' requires a path to scan.";
            return null;
        }

        var path = args[1];
        if (path.StartsWith("--", StringComparison.Ordinal))
        {
            error = $"'scan' requires a path to scan, but found the flag '{path}' where the path should be.";
            return null;
        }

        string? provenanceRange = null;
        string? provenanceRepo = null;

        for (var i = 2; i < args.Length; i++)
        {
            switch (args[i])
            {
                case "--provenance-range" when i + 1 < args.Length:
                    provenanceRange = args[++i];
                    break;
                case "--provenance-repo" when i + 1 < args.Length:
                    provenanceRepo = args[++i];
                    break;
                case "--provenance-range":
                case "--provenance-repo":
                    error = $"'{args[i]}' is missing its value.";
                    return null;
                default:
                    error = $"unrecognised argument '{args[i]}'.";
                    return null;
            }
        }

        return new ScanCommandOptions(path, provenanceRange, provenanceRepo);
    }
}
