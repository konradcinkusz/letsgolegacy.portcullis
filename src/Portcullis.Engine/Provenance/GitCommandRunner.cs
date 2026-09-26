using System.Diagnostics;
using System.Text;

namespace Portcullis.Engine.Provenance;

/// <summary>
/// Thrown whenever a `git` invocation fails to start or exits non-zero — the single
/// error type <see cref="GitProvenanceProvider"/> catches to degrade gracefully instead
/// of throwing out of <see cref="IProvenanceProvider.GetProvenance"/>.
/// </summary>
internal sealed class GitCommandException : Exception
{
    public GitCommandException(string message) : base(message) { }

    public GitCommandException(string message, Exception innerException) : base(message, innerException) { }
}

/// <summary>
/// Runs `git` as a child process and captures stdout as text. Arguments are passed via
/// <see cref="ProcessStartInfo.ArgumentList"/> (never a concatenated command string), so
/// commit messages, refs, and file paths reach git as literal argv entries regardless of
/// embedded whitespace or newlines — no shell is involved. stdout and stderr are read
/// concurrently to avoid the classic deadlock where a full stderr pipe blocks a process
/// that is still writing to stdout.
///
/// Every call pins <c>core.quotePath=false</c> and <c>diff.noprefix=false</c>, overriding
/// whatever the target repository's own config says. Without this, a path containing
/// non-ASCII characters is octal-quoted (e.g. `+++ "b/\346\227\245...cs"`, git's own
/// default) and `diff.noprefix=true` drops the `a/`/`b/` prefix entirely (`+++ Foo.cs`) —
/// both would break <see cref="GitOutputParsers"/>'s `+++` parsing silently.
/// </summary>
internal static class GitCommandRunner
{
    private static readonly string[] PinnedConfig =
        ["-c", "core.quotePath=false", "-c", "diff.noprefix=false"];

    public static string Run(string repositoryPath, params string[] arguments)
    {
        var startInfo = new ProcessStartInfo("git")
        {
            WorkingDirectory = repositoryPath,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8,
        };
        foreach (var argument in PinnedConfig)
            startInfo.ArgumentList.Add(argument);
        foreach (var argument in arguments)
            startInfo.ArgumentList.Add(argument);

        Process process;
        try
        {
            process = Process.Start(startInfo)
                ?? throw new GitCommandException($"Failed to start git in '{repositoryPath}'.");
        }
        catch (Exception ex) when (ex is not GitCommandException)
        {
            throw new GitCommandException($"Failed to start git in '{repositoryPath}': {ex.Message}", ex);
        }

        using (process)
        {
            var stdoutTask = process.StandardOutput.ReadToEndAsync();
            var stderrTask = process.StandardError.ReadToEndAsync();
            Task.WaitAll(stdoutTask, stderrTask);
            process.WaitForExit();

            if (process.ExitCode != 0)
            {
                throw new GitCommandException(
                    $"git {string.Join(' ', arguments)} exited {process.ExitCode}: {stderrTask.Result.Trim()}");
            }

            return stdoutTask.Result;
        }
    }
}
