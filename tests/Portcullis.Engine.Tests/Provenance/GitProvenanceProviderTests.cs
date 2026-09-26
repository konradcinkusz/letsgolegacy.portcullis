using Portcullis.Engine.Provenance;

namespace Portcullis.Engine.Tests.Provenance;

public class GitProvenanceProviderTests
{
    [Fact]
    public void GetProvenance_CommitWithClaudeCodeCoAuthorTrailer_IsClassifiedAiViaCommitTrailer()
    {
        using var repo = new TestGitRepository();
        repo.WriteAndCommit("Foo.cs", "public class Foo\n{\n}\n",
            author: "A Human <human@example.com>",
            message: "Add Foo\n\nCo-authored-by: Claude <noreply@anthropic.com>\n");

        var report = new GitProvenanceProvider(repo.Path).GetProvenance("HEAD");

        var range = Assert.Single(report.Ranges);
        Assert.Equal("Foo.cs", range.FilePath);
        Assert.Equal(1, range.StartLine);
        Assert.Equal(3, range.EndLine);
        Assert.Equal(ProvenanceSource.Ai, range.Source);
        Assert.Equal("claude-code", range.Attribution.Tool);
        Assert.Equal("commit-trailer", range.Attribution.Method);
        Assert.Equal(repo.LastCommitSha, range.Attribution.CommitSha);
        Assert.True(range.Confidence is > 0.0 and <= 1.0);
        Assert.Equal("1.0.0", report.SchemaVersion);
        Assert.Equal("HEAD", report.Commit);
    }

    [Fact]
    public void GetProvenance_CommitWithGitHubCopilotVsCodeCoAuthorTrailer_IsClassifiedAi()
    {
        using var repo = new TestGitRepository();
        repo.WriteAndCommit("Bar.cs", "public class Bar { }\n",
            author: "A Human <human@example.com>",
            message: "Add Bar\n\nCo-authored-by: Copilot <copilot@github.com>\n");

        var report = new GitProvenanceProvider(repo.Path).GetProvenance("HEAD");

        var range = Assert.Single(report.Ranges);
        Assert.Equal(ProvenanceSource.Ai, range.Source);
        Assert.Equal("github-copilot", range.Attribution.Tool);
        Assert.Equal("commit-trailer", range.Attribution.Method);
    }

    [Fact]
    public void GetProvenance_CommitAuthoredByCopilotCodingAgentBotIdentity_IsClassifiedAiViaBlameAuthorship()
    {
        using var repo = new TestGitRepository();
        repo.WriteAndCommit("Baz.cs", "public class Baz { }\n",
            author: "copilot-swe-agent[bot] <198982749+Copilot@users.noreply.github.com>",
            message: "Autonomous fix, no human co-author trailer");

        var report = new GitProvenanceProvider(repo.Path).GetProvenance("HEAD");

        var range = Assert.Single(report.Ranges);
        Assert.Equal(ProvenanceSource.Ai, range.Source);
        Assert.Equal("github-copilot", range.Attribution.Tool);
        Assert.Equal("git-blame-authorship", range.Attribution.Method);
        Assert.True(range.Confidence >= 0.9);
    }

    [Fact]
    public void GetProvenance_CommitAuthoredByCursorAgentIdentity_IsClassifiedAi()
    {
        using var repo = new TestGitRepository();
        repo.WriteAndCommit("Qux.cs", "public class Qux { }\n",
            author: "Cursor Agent <cursoragent@cursor.com>",
            message: "Agent commit, no co-author trailer");

        var report = new GitProvenanceProvider(repo.Path).GetProvenance("HEAD");

        var range = Assert.Single(report.Ranges);
        Assert.Equal(ProvenanceSource.Ai, range.Source);
        Assert.Equal("cursor", range.Attribution.Tool);
        Assert.Equal("git-blame-authorship", range.Attribution.Method);
    }

    [Fact]
    public void GetProvenance_CommitWithCursorMadeWithTrailer_IsClassifiedAiViaCommitTrailer()
    {
        using var repo = new TestGitRepository();
        repo.WriteAndCommit("MadeWith.cs", "public class MadeWith { }\n",
            author: "A Human <human@example.com>",
            message: "Add MadeWith\n\nMade-with: Cursor\n");

        var report = new GitProvenanceProvider(repo.Path).GetProvenance("HEAD");

        var range = Assert.Single(report.Ranges);
        Assert.Equal(ProvenanceSource.Ai, range.Source);
        Assert.Equal("cursor", range.Attribution.Tool);
        Assert.Equal("commit-trailer", range.Attribution.Method);
    }

    [Fact]
    public void GetProvenance_PlainHumanCommitWithNoTrailer_IsClassifiedHuman()
    {
        using var repo = new TestGitRepository();
        repo.WriteAndCommit("Human.cs", "public class Human { }\n",
            author: "A Human <human@example.com>",
            message: "Plain commit, no trailer");

        var report = new GitProvenanceProvider(repo.Path).GetProvenance("HEAD");

        var range = Assert.Single(report.Ranges);
        Assert.Equal(ProvenanceSource.Human, range.Source);
        Assert.Null(range.Attribution.Tool);
        Assert.Equal("git-blame-authorship", range.Attribution.Method);
    }

    [Fact]
    public void GetProvenance_CommitWithBothAiAndDistinctHumanCoAuthorTrailers_IsClassifiedMixed()
    {
        using var repo = new TestGitRepository();
        repo.WriteAndCommit("Mixed.cs", "public class Mixed { }\n",
            author: "A Human <human@example.com>",
            message: "Pair session\n\n" +
                      "Co-authored-by: Claude <noreply@anthropic.com>\n" +
                      "Co-authored-by: Another Human <other@example.com>\n");

        var report = new GitProvenanceProvider(repo.Path).GetProvenance("HEAD");

        var range = Assert.Single(report.Ranges);
        Assert.Equal(ProvenanceSource.Mixed, range.Source);
        Assert.Equal("claude-code", range.Attribution.Tool);
    }

    [Fact]
    public void GetProvenance_CommitAuthoredByUnrecognizedBotIdentity_IsClassifiedUnknownRatherThanGuessed()
    {
        using var repo = new TestGitRepository();
        repo.WriteAndCommit("Bot.cs", "public class Bot { }\n",
            author: "some-other-tool[bot] <bot@example.com>",
            message: "Unrecognized automation commit, no trailer, no known tool identity");

        var report = new GitProvenanceProvider(repo.Path).GetProvenance("HEAD");

        var range = Assert.Single(report.Ranges);
        Assert.Equal(ProvenanceSource.Unknown, range.Source);
        Assert.Null(range.Attribution.Tool);
    }

    [Fact]
    public void GetProvenance_RangeWhereLaterCommitOverwritesEarlierLine_AttributesOnlyTheChangedLineToTheLatestCommit()
    {
        using var repo = new TestGitRepository();
        repo.WriteAndCommit(
            "Overwrite.cs",
            "public class Overwrite\n{\n    int A;\n    int B;\n    int C;\n}\n",
            author: "A Human <human@example.com>",
            message: "Initial human commit");
        var baseSha = repo.LastCommitSha;

        repo.WriteAndCommit(
            "Overwrite.cs",
            "public class Overwrite\n{\n    int A;\n    int BChanged;\n    int C;\n}\n",
            author: "A Human <human@example.com>",
            message: "AI fixup\n\nCo-authored-by: Claude <noreply@anthropic.com>\n");
        var tipSha = repo.LastCommitSha;

        var report = new GitProvenanceProvider(repo.Path).GetProvenance($"{baseSha}..HEAD");

        var range = Assert.Single(report.Ranges);
        Assert.Equal("Overwrite.cs", range.FilePath);
        Assert.Equal(4, range.StartLine);
        Assert.Equal(4, range.EndLine);
        Assert.Equal(ProvenanceSource.Ai, range.Source);
        Assert.Equal("claude-code", range.Attribution.Tool);
        Assert.Equal(tipSha, range.Attribution.CommitSha);
    }

    [Fact]
    public void GetProvenance_UnresolvableCommitOrRange_ReturnsEmptyReportRatherThanThrowing()
    {
        using var repo = new TestGitRepository();
        repo.WriteAndCommit("Foo.cs", "public class Foo { }\n",
            author: "A Human <human@example.com>", message: "Initial");

        var report = new GitProvenanceProvider(repo.Path).GetProvenance("does-not-exist-1234");

        Assert.Empty(report.Ranges);
        Assert.Equal("1.0.0", report.SchemaVersion);
    }

    [Fact]
    public void GetProvenance_NonGitDirectory_ReturnsEmptyReportRatherThanThrowing()
    {
        var tempDir = Directory.CreateTempSubdirectory("portcullis-provenance-nongit-");
        try
        {
            var report = new GitProvenanceProvider(tempDir.FullName).GetProvenance("HEAD");

            Assert.Empty(report.Ranges);
        }
        finally
        {
            tempDir.Delete(recursive: true);
        }
    }

    [Fact]
    public void GetProvenance_CommitTouchingTwoFiles_ProducesRangesSortedByFilePath()
    {
        using var repo = new TestGitRepository();
        repo.WriteAndCommit("A.cs", "class A { }\n", author: "A Human <human@example.com>", message: "seed A");
        repo.WriteAndCommit("Z.cs", "class Z { }\n", author: "A Human <human@example.com>", message: "seed Z");
        var baseSha = repo.LastCommitSha;

        // Stage Z.cs directly, then commit it together with A.cs's change so this one
        // commit genuinely touches both files.
        File.WriteAllText(Path.Combine(repo.Path, "Z.cs"), "class Z { }\n// touched\n");
        repo.WriteAndCommit("A.cs", "class A { }\n// touched\n",
            author: "A Human <human@example.com>", message: "touch both files");

        var report = new GitProvenanceProvider(repo.Path).GetProvenance($"{baseSha}..HEAD");

        Assert.Equal(2, report.Ranges.Count);
        Assert.Equal(["A.cs", "Z.cs"], report.Ranges.Select(r => r.FilePath).ToArray());
    }

    [Fact]
    public void GetProvenance_FileNameWithNonAsciiCharacters_IsNotMisattributedOrDropped()
    {
        // Git's own default (core.quotePath=true) octal-quotes non-ASCII paths in diff
        // headers (e.g. `+++ "b/\346\227\245..."`); GitCommandRunner pins
        // core.quotePath=false so this parses like any other path. Regression coverage
        // for that fix: a plain-ASCII file plus a non-ASCII file in the same commit, both
        // must resolve to their own correct range rather than one clobbering the other.
        using var repo = new TestGitRepository();
        repo.WriteAndCommit("Ascii.cs", "class Ascii { }\n",
            author: "A Human <human@example.com>", message: "seed Ascii");
        var baseSha = repo.LastCommitSha;

        File.WriteAllText(Path.Combine(repo.Path, "Ascii.cs"), "class Ascii { }\n// touched\n");
        repo.WriteAndCommit("日本語.cs", "class Nihongo { }\n",
            author: "A Human <human@example.com>",
            message: "touch Ascii and add a non-ASCII-named file\n\nCo-authored-by: Claude <noreply@anthropic.com>\n");

        var report = new GitProvenanceProvider(repo.Path).GetProvenance($"{baseSha}..HEAD");

        Assert.Equal(2, report.Ranges.Count);
        Assert.Equal(["Ascii.cs", "日本語.cs"], report.Ranges.Select(r => r.FilePath).ToArray());
        Assert.All(report.Ranges, r => Assert.Equal(ProvenanceSource.Ai, r.Source));
    }

    [Fact]
    public void GetProvenance_RepositoryConfiguredWithDiffNoprefix_StillParsesCorrectly()
    {
        // GitCommandRunner pins diff.noprefix=false on every invocation regardless of the
        // target repository's own config; without that override, a repo with
        // diff.noprefix=true emits `+++ Foo.cs` (no "b/" prefix) and every range in it
        // would silently vanish. Set the repo's local config directly to simulate that
        // environment and prove the override wins.
        using var repo = new TestGitRepository();
        repo.WriteAndCommit("Foo.cs", "class Foo { }\n",
            author: "A Human <human@example.com>", message: "seed Foo");
        var baseSha = repo.LastCommitSha;
        GitCommandRunner.Run(repo.Path, "config", "diff.noprefix", "true");

        repo.WriteAndCommit("Foo.cs", "class Foo { }\n// touched\n",
            author: "A Human <human@example.com>",
            message: "touch Foo\n\nCo-authored-by: Claude <noreply@anthropic.com>\n");

        var report = new GitProvenanceProvider(repo.Path).GetProvenance($"{baseSha}..HEAD");

        var range = Assert.Single(report.Ranges);
        Assert.Equal("Foo.cs", range.FilePath);
        Assert.Equal(ProvenanceSource.Ai, range.Source);
    }

    // ---------------------------------------------------------------------------------
    // Fail-safe contract. Before these, every git failure below produced an ordinary
    // empty ProvenanceReport, indistinguishable from "this diff changed nothing" — which
    // a diff-scoped gate reads as "nothing to block on". Covered here at the provider
    // level; ScannerGateTests covers the gate consequence.
    // ---------------------------------------------------------------------------------

    [Fact]
    public void GetProvenance_RangeThatDoesNotResolve_IsReportedDegradedRatherThanEmpty()
    {
        using var repo = new TestGitRepository();
        repo.WriteAndCommit("Foo.cs", "class Foo { }\n",
            author: "A Human <human@example.com>", message: "seed Foo");

        var report = new GitProvenanceProvider(repo.Path).GetProvenance("no-such-ref..HEAD");

        Assert.Equal(ProvenanceStatus.Degraded, report.Status);
        Assert.Empty(report.Ranges);
        Assert.NotNull(report.DegradedReason);
        Assert.Contains("no-such-ref", report.DegradedReason);
    }

    [Fact]
    public void GetProvenance_PathThatIsNotAGitRepository_IsReportedDegradedRatherThanEmpty()
    {
        var tempDir = Directory.CreateTempSubdirectory("portcullis-not-a-repo-");
        try
        {
            var report = new GitProvenanceProvider(tempDir.FullName).GetProvenance("HEAD");

            Assert.Equal(ProvenanceStatus.Degraded, report.Status);
            Assert.Empty(report.Ranges);
            Assert.NotNull(report.DegradedReason);
        }
        finally
        {
            tempDir.Delete(recursive: true);
        }
    }

    [Fact]
    public void GetProvenance_RangeThatResolvesButChangedNothing_StaysCompleteWithNoRanges()
    {
        // The other half of the contract, and the reason Status exists at all: an
        // empty-but-honest answer must NOT be reported as degraded, or every no-op diff
        // would needlessly escalate the gate to whole-tree scope.
        using var repo = new TestGitRepository();
        repo.WriteAndCommit("Foo.cs", "class Foo { }\n",
            author: "A Human <human@example.com>", message: "seed Foo");

        var report = new GitProvenanceProvider(repo.Path).GetProvenance("HEAD..HEAD");

        Assert.Equal(ProvenanceStatus.Complete, report.Status);
        Assert.Empty(report.Ranges);
        Assert.Null(report.DegradedReason);
    }

    [Fact]
    public void GetProvenance_CommitThatOnlyDeletesAFile_StaysCompleteRatherThanDegraded()
    {
        // A pure deletion produces a real patch whose only hunks have no resulting-file
        // lines. That is a correct, well-formed diff — treating "patch text but zero
        // spans" as a parse failure would degrade the gate on an ordinary commit.
        using var repo = new TestGitRepository();
        repo.WriteAndCommit("Doomed.cs", "class Doomed { }\n",
            author: "A Human <human@example.com>", message: "seed Doomed");
        var baseSha = repo.LastCommitSha;

        File.Delete(Path.Combine(repo.Path, "Doomed.cs"));
        GitCommandRunner.Run(repo.Path, "add", "-A");
        GitCommandRunner.Run(repo.Path, "commit", "--author", "A Human <human@example.com>", "-m", "delete Doomed");

        var report = new GitProvenanceProvider(repo.Path).GetProvenance($"{baseSha}..HEAD");

        Assert.Equal(ProvenanceStatus.Complete, report.Status);
        Assert.Empty(report.Ranges);
    }
}
