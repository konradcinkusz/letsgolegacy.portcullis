namespace Portcullis.CiComment;

/// <summary>
/// Parsed command-line arguments for the portcullis-ci-comment entry point.
/// `--current` is the only required flag; everything else is optional.
/// </summary>
public sealed record CliOptions(string CurrentPath, string? PreviousPath, string? OutputPath, int? PullRequestNumber)
{
    public static CliOptions? Parse(string[] args)
    {
        string? current = null;
        string? previous = null;
        string? output = null;
        int? pr = null;

        for (var i = 0; i < args.Length; i++)
        {
            switch (args[i])
            {
                case "--current" when i + 1 < args.Length:
                    current = args[++i];
                    break;
                case "--previous" when i + 1 < args.Length:
                    previous = args[++i];
                    break;
                case "--output" when i + 1 < args.Length:
                    output = args[++i];
                    break;
                case "--pr" when i + 1 < args.Length:
                    if (!int.TryParse(args[++i], out var parsedPr))
                    {
                        return null;
                    }

                    pr = parsedPr;
                    break;
                default:
                    return null;
            }
        }

        return current is null ? null : new CliOptions(current, previous, output, pr);
    }
}
