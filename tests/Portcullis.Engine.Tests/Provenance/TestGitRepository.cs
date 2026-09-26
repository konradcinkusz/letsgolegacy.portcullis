using Portcullis.Engine.Provenance;

namespace Portcullis.Engine.Tests.Provenance;

/// <summary>
/// A disposable, throwaway git repository for exercising GitProvenanceProvider against
/// real commit history rather than a mock. Every commit here runs the real `git`
/// binary (via the same GitCommandRunner the provider itself uses), so trailers and
/// author identity are parsed by git exactly as they would be in any real repository.
/// </summary>
internal sealed class TestGitRepository : IDisposable
{
    private readonly DirectoryInfo _tempDir;

    public string Path => _tempDir.FullName;

    public string LastCommitSha => GitCommandRunner.Run(Path, "rev-parse", "HEAD").Trim();

    public TestGitRepository()
    {
        _tempDir = Directory.CreateTempSubdirectory("portcullis-provenance-test-");
        GitCommandRunner.Run(Path, "init");
        GitCommandRunner.Run(Path, "config", "user.name", "Test Committer");
        GitCommandRunner.Run(Path, "config", "user.email", "test-committer@example.com");
    }

    public void WriteAndCommit(string relativePath, string content, string author, string message)
    {
        var fullPath = System.IO.Path.Combine(Path, relativePath);
        var directory = System.IO.Path.GetDirectoryName(fullPath);
        if (!string.IsNullOrEmpty(directory))
            Directory.CreateDirectory(directory);
        File.WriteAllText(fullPath, content);

        GitCommandRunner.Run(Path, "add", "-A");
        GitCommandRunner.Run(Path, "commit", "--author", author, "-m", message);
    }

    public void Dispose() => _tempDir.Delete(recursive: true);
}
