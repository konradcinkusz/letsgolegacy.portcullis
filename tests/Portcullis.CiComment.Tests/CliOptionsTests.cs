namespace Portcullis.CiComment.Tests;

public class CliOptionsTests
{
    [Fact]
    public void Parse_RequiresCurrentFlag()
    {
        var options = CliOptions.Parse([]);

        Assert.Null(options);
    }

    [Fact]
    public void Parse_CurrentOnly_LeavesEverythingElseNull()
    {
        var options = CliOptions.Parse(["--current", "scan.json"]);

        Assert.NotNull(options);
        Assert.Equal("scan.json", options.CurrentPath);
        Assert.Null(options.PreviousPath);
        Assert.Null(options.OutputPath);
        Assert.Null(options.PullRequestNumber);
    }

    [Fact]
    public void Parse_AllFlags_PopulatesEveryField()
    {
        var options = CliOptions.Parse(
            ["--current", "current.json", "--previous", "previous.json", "--output", "comment.md", "--pr", "42"]);

        Assert.NotNull(options);
        Assert.Equal("current.json", options.CurrentPath);
        Assert.Equal("previous.json", options.PreviousPath);
        Assert.Equal("comment.md", options.OutputPath);
        Assert.Equal(42, options.PullRequestNumber);
    }

    [Fact]
    public void Parse_NonNumericPrValue_FailsRatherThanSilentlyIgnoringIt()
    {
        var options = CliOptions.Parse(["--current", "scan.json", "--pr", "not-a-number"]);

        Assert.Null(options);
    }

    [Fact]
    public void Parse_UnknownFlag_FailsRatherThanSilentlyIgnoringIt()
    {
        var options = CliOptions.Parse(["--current", "scan.json", "--bogus", "value"]);

        Assert.Null(options);
    }

    [Fact]
    public void Parse_FlagMissingItsValue_FailsRatherThanThrowing()
    {
        var options = CliOptions.Parse(["--current"]);

        Assert.Null(options);
    }
}
