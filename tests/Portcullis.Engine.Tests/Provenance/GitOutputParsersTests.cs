using Portcullis.Engine.Provenance;

namespace Portcullis.Engine.Tests.Provenance;

/// <summary>
/// Covers the one diff-side failure git itself does not signal: a patch this parser
/// cannot fully read. git exits 0, nothing throws, and the only symptom is a span list
/// with real changed lines missing from it — which, under a diff-scoped gate, is a gate
/// that silently stops enforcing. These tests pin the boundary between that case and the
/// two well-formed shapes that also legitimately produce no spans, since collapsing them
/// would degrade the gate on ordinary commits.
///
/// Driven with literal patch text rather than a real repository on purpose: git cannot be
/// made to emit an unparseable header on demand, so the malformed case is only reachable
/// by handing the parser the bytes directly.
/// </summary>
public class GitOutputParsersTests
{
    [Fact]
    public void ParseAddedSpans_WellFormedPatch_ReturnsSpansAndReportsNoParseFailure()
    {
        const string patch = """
            diff --git a/Foo.cs b/Foo.cs
            index 1111111..2222222 100644
            --- a/Foo.cs
            +++ b/Foo.cs
            @@ -0,0 +1,3 @@
            +class Foo
            +{
            +}
            """;

        var parse = GitOutputParsers.ParseAddedSpans(patch);

        var span = Assert.Single(parse.Spans);
        Assert.Equal("Foo.cs", span.FilePath);
        Assert.Equal(1, span.StartLine);
        Assert.Equal(3, span.EndLine);
        Assert.False(parse.SawUnattributedHunk);
    }

    [Fact]
    public void ParseAddedSpans_HunkWithNoParseableFileHeader_ReportsAnUnattributedHunk()
    {
        // The "+++" line is present but in a shape this parser does not recognise — here
        // the prefix-less form a repo with diff.noprefix=true emits, which is exactly what
        // GitCommandRunner's pinned config exists to prevent. The hunk below it has no
        // file to belong to and its changed lines are dropped, which is the fail-open
        // shape: silently fewer ranges means a silently weaker gate.
        const string patch = """
            diff --git a/Foo.cs b/Foo.cs
            index 1111111..2222222 100644
            --- Foo.cs
            +++ Foo.cs
            @@ -0,0 +1,3 @@
            +class Foo
            +{
            +}
            """;

        var parse = GitOutputParsers.ParseAddedSpans(patch);

        Assert.Empty(parse.Spans);
        Assert.True(parse.SawUnattributedHunk);
    }

    [Fact]
    public void ParseAddedSpans_PureDeletion_ReportsNoSpansAndNoParseFailure()
    {
        // "+++ /dev/null" parses fine and correctly yields nothing: the file is gone, so
        // it has no resulting-file lines to attribute. Must not be mistaken for the
        // malformed case above — a deletion-only commit is completely ordinary.
        const string patch = """
            diff --git a/Doomed.cs b/Doomed.cs
            deleted file mode 100644
            index 1111111..0000000
            --- a/Doomed.cs
            +++ /dev/null
            @@ -1,3 +0,0 @@
            -class Doomed
            -{
            -}
            """;

        var parse = GitOutputParsers.ParseAddedSpans(patch);

        Assert.Empty(parse.Spans);
        Assert.False(parse.SawUnattributedHunk);
    }

    [Fact]
    public void ParseAddedSpans_BinaryFileSection_ReportsNoSpansAndNoParseFailure()
    {
        // A binary section carries no "@@" hunks at all, so there is nothing to attribute
        // and nothing was lost.
        const string patch = """
            diff --git a/logo.png b/logo.png
            index 1111111..2222222 100644
            Binary files a/logo.png and b/logo.png differ
            """;

        var parse = GitOutputParsers.ParseAddedSpans(patch);

        Assert.Empty(parse.Spans);
        Assert.False(parse.SawUnattributedHunk);
    }

    [Fact]
    public void ParseAddedSpans_UnreadableHeaderFollowedByAGoodFile_KeepsTheGoodSpansAndStillReportsTheFailure()
    {
        // The failure must not be swallowed just because other files parsed. A partially
        // readable patch is still an incomplete changed-line set.
        const string patch = """
            diff --git a/Bad.cs b/Bad.cs
            +++ Bad.cs
            @@ -0,0 +1,2 @@
            +class Bad
            +{ }
            diff --git a/Good.cs b/Good.cs
            --- a/Good.cs
            +++ b/Good.cs
            @@ -4,0 +5,2 @@
            +// added
            +// lines
            """;

        var parse = GitOutputParsers.ParseAddedSpans(patch);

        var span = Assert.Single(parse.Spans);
        Assert.Equal("Good.cs", span.FilePath);
        Assert.Equal(5, span.StartLine);
        Assert.Equal(6, span.EndLine);
        Assert.True(parse.SawUnattributedHunk);
    }

    [Fact]
    public void ParseAddedSpans_QuotedPathHeader_IsParsedAndUnescapedRatherThanTreatedAsUnknown()
    {
        // git C-quotes any path containing a double quote, a backslash or a control
        // character, and does so unconditionally — core.quotePath=false only governs
        // non-ASCII bytes. Treating that ordinary output as an unreadable header degraded
        // the whole gate to whole-tree scope for every PR touching such a file.
        const string patch = """
            diff --git "a/SD/we\"ird.cs" "b/SD/we\"ird.cs"
            index 1111111..2222222 100644
            --- "a/SD/we\"ird.cs"
            +++ "b/SD/we\"ird.cs"
            @@ -1 +1,2 @@
            +class A { }
            +// added
            """;

        var parse = GitOutputParsers.ParseAddedSpans(patch);

        var span = Assert.Single(parse.Spans);
        Assert.Equal("SD/we\"ird.cs", span.FilePath);
        Assert.False(parse.SawUnattributedHunk);
    }

    [Fact]
    public void ParseAddedSpans_QuotedPathWithABackslash_IsUnescaped()
    {
        const string patch = """
            diff --git "a/SD/back\\slash.cs" "b/SD/back\\slash.cs"
            --- "a/SD/back\\slash.cs"
            +++ "b/SD/back\\slash.cs"
            @@ -0,0 +1 @@
            +class A { }
            """;

        var parse = GitOutputParsers.ParseAddedSpans(patch);

        var span = Assert.Single(parse.Spans);
        Assert.Equal("SD/back\\slash.cs", span.FilePath);
        Assert.False(parse.SawUnattributedHunk);
    }

    [Fact]
    public void ParseAddedSpans_QuotedPathWithOctalEscapes_DecodesThemAsUtf8Bytes()
    {
        // Octal escapes are bytes, not characters: one non-ASCII character arrives as
        // several, and decoding them individually would produce mojibake rather than the
        // filename. "\346\227\245" is U+65E5.
        const string patch = """
            diff --git "a/SD/\346\227\245.cs" "b/SD/\346\227\245.cs"
            --- "a/SD/\346\227\245.cs"
            +++ "b/SD/\346\227\245.cs"
            @@ -0,0 +1 @@
            +class A { }
            """;

        var parse = GitOutputParsers.ParseAddedSpans(patch);

        var span = Assert.Single(parse.Spans);
        Assert.Equal("SD/\u65e5.cs", span.FilePath);
        Assert.False(parse.SawUnattributedHunk);
    }
}
