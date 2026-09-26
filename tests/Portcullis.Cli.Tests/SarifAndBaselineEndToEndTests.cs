using System.Diagnostics;
using System.Text.Json.Nodes;

namespace Portcullis.Cli.Tests;

/// <summary>
/// `portcullis scan` with --sarif, --baseline and --write-baseline, run as the real process
/// against real files and a real git history — exit codes, files written and stderr included,
/// since those are what a CI workflow actually sees (docs/SARIF.md).
/// </summary>
public class SarifAndBaselineEndToEndTests : IDisposable
{
    // PORTCULLIS_MIG_CONFIGURATION_MANAGER, an error, on line 7.
    private static string SettingsClass(string name) => $$"""
        using System.Configuration;

        namespace Shop;

        public static class {{name}}
        {
            public static string Read() => ConfigurationManager.AppSettings["{{name}}"];
        }
        """;

    private readonly DirectoryInfo _repo = Directory.CreateTempSubdirectory("portcullis-cli-sarif-");

    public void Dispose() => _repo.Delete(recursive: true);

    private string InRepo(string relative) => Path.Combine(_repo.FullName, relative);

    private void Write(string relative, string content)
    {
        var path = InRepo(relative);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, content);
    }

    private static async Task<(int ExitCode, string Stdout, string Stderr)> RunAsync(string program, string workingDirectory, params string[] arguments)
    {
        var startInfo = new ProcessStartInfo(program)
        {
            WorkingDirectory = workingDirectory,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };
        foreach (var argument in arguments)
        {
            startInfo.ArgumentList.Add(argument);
        }

        using var process = Process.Start(startInfo)!;
        var stdout = process.StandardOutput.ReadToEndAsync();
        var stderr = process.StandardError.ReadToEndAsync();
        await process.WaitForExitAsync();
        return (process.ExitCode, await stdout, await stderr);
    }

    // The CLI this test project builds, run through the same dotnet host running the tests.
    private Task<(int ExitCode, string Stdout, string Stderr)> PortcullisAsync(params string[] arguments) =>
        RunAsync(
            Environment.GetEnvironmentVariable("DOTNET_HOST_PATH") ?? "dotnet",
            _repo.FullName,
            [Path.Combine(AppContext.BaseDirectory, "Portcullis.Cli.dll"), .. arguments]);

    private async Task<string> GitAsync(params string[] arguments)
    {
        var (exitCode, stdout, stderr) = await RunAsync("git", _repo.FullName, arguments);
        Assert.True(exitCode == 0, $"git {string.Join(' ', arguments)} failed: {stderr}");
        return stdout.Trim();
    }

    private async Task<string> CommitAllAsync(string message)
    {
        await GitAsync("add", "-A");
        await GitAsync("-c", "user.name=Test", "-c", "user.email=test@example.com", "commit", "-q", "-m", message);
        return await GitAsync("rev-parse", "HEAD");
    }

    private static JsonArray ResultsOf(string sarifPath) =>
        (JsonArray)JsonNode.Parse(File.ReadAllText(sarifPath))!["runs"]![0]!["results"]!;

    private static string UriOf(JsonNode? result) =>
        (string)result!["locations"]![0]!["physicalLocation"]!["artifactLocation"]!["uri"]!;

    [Fact]
    public async Task WriteBaseline_ThenBaseline_OnlyANewErrorBlocks_AndOnlyItIsInTheSarif()
    {
        Write("src/Shop/Settings.cs", SettingsClass("Settings"));

        var recorded = await PortcullisAsync("scan", "src", "--baseline", "portcullis-baseline.json", "--write-baseline");
        Assert.Equal(0, recorded.ExitCode);
        Assert.Contains("wrote 2 finding(s) to the baseline 'portcullis-baseline.json'", recorded.Stderr);
        Assert.Contains("Shop/Settings.cs", File.ReadAllText(InRepo("portcullis-baseline.json")));

        var unchanged = await PortcullisAsync("scan", "src", "--baseline", "portcullis-baseline.json");
        Assert.Equal(0, unchanged.ExitCode);

        Write("src/Shop/Pricing.cs", SettingsClass("Pricing"));
        var introduced = await PortcullisAsync(
            "scan", "src", "--baseline", "portcullis-baseline.json", "--provenance-repo", ".", "--sarif", "out/portcullis.sarif");

        Assert.Equal(1, introduced.ExitCode);
        var result = Assert.Single(ResultsOf(InRepo("out/portcullis.sarif")));
        Assert.Equal("src/Shop/Pricing.cs", UriOf(result));
        Assert.Equal("new", (string)result!["baselineState"]!);
        var gate = JsonNode.Parse(introduced.Stdout)!["gate"]!;
        Assert.Equal(1, (int)gate["blockingErrorCount"]!);
        Assert.Equal(1, (int)gate["acceptedByBaselineCount"]!);
    }

    [Fact]
    public async Task DiffScopedSarif_FromARealGitRange_HoldsOnlyTheFindingsOnItsChangedLines()
    {
        await GitAsync("init", "-q");
        Write("src/Shop/Settings.cs", SettingsClass("Settings"));
        var before = await CommitAllAsync("An error that predates the pull request");
        Write("src/Shop/Pricing.cs", SettingsClass("Pricing"));
        var after = await CommitAllAsync("The pull request adds another");

        var run = await PortcullisAsync(
            "scan", "src", "--provenance-range", $"{before}..{after}", "--provenance-repo", ".", "--sarif", "pr.sarif");

        Assert.Equal(1, run.ExitCode);
        var result = Assert.Single(ResultsOf(InRepo("pr.sarif")));
        Assert.Equal("src/Shop/Pricing.cs", UriOf(result));
        Assert.Equal(7, (int)result!["locations"]![0]!["physicalLocation"]!["region"]!["startLine"]!);
        var properties = JsonNode.Parse(File.ReadAllText(InRepo("pr.sarif")))!["runs"]![0]!["properties"]!;
        Assert.Equal("diff", (string?)properties["scope"]);
        Assert.True((int)properties["excludedOutsideChangedLines"]! >= 1);
    }

    [Fact]
    public async Task MissingBaselineFile_StopsTheRunWithAUsageErrorAndSaysHowToCreateOne()
    {
        Write("src/Shop/Settings.cs", SettingsClass("Settings"));

        var run = await PortcullisAsync("scan", "src", "--baseline", "absent.json");

        Assert.Equal(2, run.ExitCode);
        Assert.Contains("does not exist", run.Stderr);
        Assert.Contains("--write-baseline", run.Stderr);
        Assert.Equal(string.Empty, run.Stdout);
    }

    [Fact]
    public async Task ASarifPathThatCannotBeWritten_IsAUsageErrorNotAPassingRun()
    {
        Write("src/Shop/Settings.cs", SettingsClass("Settings"));
        Directory.CreateDirectory(InRepo("occupied"));

        var run = await PortcullisAsync("scan", "src", "--sarif", "occupied");

        Assert.Equal(2, run.ExitCode);
        Assert.Contains("could not write the SARIF file", run.Stderr);
    }
}
