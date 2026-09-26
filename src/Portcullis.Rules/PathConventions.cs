namespace Portcullis.Rules;

/// <summary>
/// The one implementation of "does this file path sit under a folder the team calls X".
///
/// Extracted because <see cref="KernelBoundaryAnalyzer"/> and
/// <see cref="AntiCorruptionEdgeAnalyzer"/> each carried a private copy of the identical
/// matcher, and <see cref="ConventionCoverageAnalyzer"/> needs a third. Two copies were
/// tolerable; three would guarantee drift, and drift here is worse than ordinary
/// duplication — the coverage diagnostic's whole job is to report whether the *other*
/// rules matched anything, so if it matched by even slightly different logic it would
/// confidently report the wrong answer.
/// </summary>
internal static class PathConventions
{
    // netstandard2.0 (required for analyzers — see Portcullis.Rules.csproj) has no
    // Split(params char[], StringSplitOptions) overload, so the separators are a field
    // rather than inline arguments. Do not "modernize" this to Split('/', '\\', ...):
    // it compiles only on netstandard2.1+/net5+, and switching this project's target to
    // get it would stop the analyzer loading in Visual Studio.
    private static readonly char[] PathSeparators = ['/', '\\'];

    /// <summary>
    /// A path segment matches a convention name either exactly ("ServiceDefaults") or as
    /// a project-name suffix ("Consumer.ServiceDefaults", "Acme.ServiceDefaults") — the
    /// latter is the shape architecture-standards' own worked examples and the reference
    /// consumer app both actually use; requiring an exact segment match would never match
    /// either.
    /// </summary>
    public static bool SegmentMatches(string part, string name) =>
        part.Equals(name, StringComparison.OrdinalIgnoreCase)
        || part.EndsWith("." + name, StringComparison.OrdinalIgnoreCase);

    public static bool HasSegment(string? filePath, IEnumerable<string> segments)
        => MatchedSegment(filePath, segments) is not null;

    /// <summary>
    /// The first path segment matching any configured name, or null when none does.
    /// Returning the segment rather than a bool lets a caller name the folder it matched
    /// in a diagnostic message.
    /// </summary>
    public static string? MatchedSegment(string? filePath, IEnumerable<string> segments)
    {
        // Written as an explicit null/length test rather than string.IsNullOrEmpty:
        // netstandard2.0's reference assembly carries no [NotNullWhen(false)] annotation
        // on IsNullOrEmpty, so the compiler cannot narrow filePath afterwards and the
        // Split below warns CS8602. This form flows correctly on every target.
        if (filePath is null || filePath.Length == 0)
        {
            return null;
        }

        var parts = filePath.Split(PathSeparators, StringSplitOptions.RemoveEmptyEntries);
        foreach (var part in parts)
        {
            foreach (var segment in segments)
            {
                if (SegmentMatches(part, segment))
                {
                    return part;
                }
            }
        }

        return null;
    }

    /// <summary>
    /// True when the file's own name (not its folders) matches one of the configured
    /// names, compared case-insensitively — the shape <see cref="ObservabilityBuildTimeAnalyzer"/>
    /// uses to recognise a service entry point.
    /// </summary>
    public static bool HasFileName(string? filePath, IEnumerable<string> fileNames)
    {
        if (filePath is null || filePath.Length == 0)
        {
            return false;
        }

        var parts = filePath.Split(PathSeparators, StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length == 0)
        {
            return false;
        }

        var fileName = parts[parts.Length - 1];
        foreach (var candidate in fileNames)
        {
            if (fileName.Equals(candidate, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }
}
