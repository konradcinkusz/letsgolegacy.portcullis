using Portcullis.Cli;

namespace Portcullis.Cli.Tests;

/// <summary>
/// The `portcullis scan` argument parser. This project exists because there was no test
/// project for Portcullis.Cli at all, which is precisely why two silent mis-parses survived
/// in the old inline loop — both of which quietly changed the merge gate's scope without
/// telling anyone. The first two tests below are those exact cases; each one passed
/// (wrongly, and silently) before <see cref="ScanCommandOptions"/> replaced that loop.
/// </summary>
public class ScanCommandOptionsTests
{
    [Fact]
    public void Parse_TrailingProvenanceRangeFlagWithNoValue_IsRejectedRatherThanIgnored()
    {
        // The old loop ran to `args.Length - 1`, so the final element was never examined
        // as a flag name. This argv silently scanned with no provenance at all, flipping
        // Gate.Scope from "diff" to "all" with no diagnostic anywhere.
        var options = ScanCommandOptions.Parse(["scan", "/repo", "--provenance-range"], out var error);

        Assert.Null(options);
        Assert.Contains("--provenance-range", error);
        Assert.Contains("missing its value", error);
    }

    [Fact]
    public void Parse_MisspelledFlag_IsRejectedRatherThanIgnored()
    {
        // The old loop had no `default:` arm, so a typo in a consumer's workflow silently
        // produced a gate scoped differently from the one they wrote.
        var options = ScanCommandOptions.Parse(
            ["scan", "/repo", "--provenence-range", "HEAD~1..HEAD"], out var error);

        Assert.Null(options);
        Assert.Contains("--provenence-range", error);
    }

    [Fact]
    public void Parse_PathAndBothProvenanceFlags_ParsesEveryValue()
    {
        var options = ScanCommandOptions.Parse(
            ["scan", "/repo/src", "--provenance-range", "abc..def", "--provenance-repo", "/repo"], out var error);

        Assert.NotNull(options);
        Assert.Equal(string.Empty, error);
        Assert.Equal("/repo/src", options.Path);
        Assert.Equal("abc..def", options.ProvenanceRange);
        Assert.Equal("/repo", options.ProvenanceRepo);
    }

    [Fact]
    public void Parse_PathOnly_ParsesWithNoProvenance()
    {
        var options = ScanCommandOptions.Parse(["scan", "/repo"], out var error);

        Assert.NotNull(options);
        Assert.Equal(string.Empty, error);
        Assert.Equal("/repo", options.Path);
        Assert.Null(options.ProvenanceRange);
        Assert.Null(options.ProvenanceRepo);
    }

    [Fact]
    public void Parse_FlagsInEitherOrder_ParsesTheSame()
    {
        var reversed = ScanCommandOptions.Parse(
            ["scan", "/repo", "--provenance-repo", "/repo", "--provenance-range", "abc..def"], out _);

        Assert.NotNull(reversed);
        Assert.Equal("abc..def", reversed.ProvenanceRange);
        Assert.Equal("/repo", reversed.ProvenanceRepo);
    }

    [Fact]
    public void Parse_NoArguments_IsRejected()
    {
        Assert.Null(ScanCommandOptions.Parse([], out var error));
        Assert.NotEqual(string.Empty, error);
    }

    [Fact]
    public void Parse_UnknownCommand_IsRejected()
    {
        Assert.Null(ScanCommandOptions.Parse(["analyse", "/repo"], out var error));
        Assert.Contains("scan", error);
    }

    [Fact]
    public void Parse_ScanWithNoPath_IsRejected()
    {
        Assert.Null(ScanCommandOptions.Parse(["scan"], out var error));
        Assert.Contains("requires a path", error);
    }

    [Fact]
    public void Parse_FlagWhereThePathShouldBe_IsRejectedRatherThanTreatedAsAPath()
    {
        // Without this, `portcullis scan --provenance-range HEAD` would scan a directory
        // literally named "--provenance-range", find nothing, and exit 0.
        var options = ScanCommandOptions.Parse(["scan", "--provenance-range", "HEAD"], out var error);

        Assert.Null(options);
        Assert.Contains("requires a path", error);
    }

    [Fact]
    public void Parse_ExtraPositionalArgument_IsRejected()
    {
        Assert.Null(ScanCommandOptions.Parse(["scan", "/repo", "/other"], out var error));
        Assert.Contains("/other", error);
    }
}
