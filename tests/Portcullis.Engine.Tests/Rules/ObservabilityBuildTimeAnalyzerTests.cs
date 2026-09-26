using Portcullis.Rules;

namespace Portcullis.Engine.Tests.Rules;

public class ObservabilityBuildTimeAnalyzerTests
{
    [Fact]
    public async Task FiresWhenAWebAppProgramNeverCallsAddServiceDefaults()
    {
        const string source = """
            var builder = WebApplication.CreateBuilder(args);

            builder.Services.AddControllers();

            var app = builder.Build();
            app.Run();
            """;

        var diagnostics = await AnalyzerTestHelper.GetDiagnosticsAsync(
            new ObservabilityBuildTimeAnalyzer(),
            ("src/Demo.AdvertsService/Program.cs", source));

        var hit = Assert.Single(diagnostics, d => d.Id == ObservabilityBuildTimeAnalyzer.MissingServiceDefaultsRule.Id);
        Assert.Contains("Program.cs", hit.GetMessage());
    }

    [Fact]
    public async Task DoesNotFireWhenAWebAppProgramCallsAddServiceDefaults()
    {
        const string source = """
            var builder = WebApplication.CreateBuilder(args);

            builder.AddServiceDefaults();
            builder.Services.AddControllers();

            var app = builder.Build();
            app.Run();
            """;

        var diagnostics = await AnalyzerTestHelper.GetDiagnosticsAsync(
            new ObservabilityBuildTimeAnalyzer(),
            ("src/Demo.AdvertsService/Program.cs", source));

        Assert.DoesNotContain(diagnostics, d => d.Id == ObservabilityBuildTimeAnalyzer.MissingServiceDefaultsRule.Id);
    }

    [Fact]
    public async Task DoesNotFireForAWorkerJobThatCallsAddServiceDefaults()
    {
        // The real, current shape of the reference consumer app's Consumer.Jobs.PromotionExpiry/Program.cs.
        const string source = """
            var builder = Host.CreateApplicationBuilder(args);

            builder.AddServiceDefaults();

            var host = builder.Build();
            await host.RunAsync();
            """;

        var diagnostics = await AnalyzerTestHelper.GetDiagnosticsAsync(
            new ObservabilityBuildTimeAnalyzer(),
            ("src/Demo.Jobs.Expiry/Program.cs", source));

        Assert.DoesNotContain(diagnostics, d => d.Id == ObservabilityBuildTimeAnalyzer.MissingServiceDefaultsRule.Id);
    }

    [Fact]
    public async Task DoesNotFireWhenTheFileIsNotNamedProgramCs()
    {
        const string source = """
            var builder = WebApplication.CreateBuilder(args);
            var app = builder.Build();
            app.Run();
            """;

        var diagnostics = await AnalyzerTestHelper.GetDiagnosticsAsync(
            new ObservabilityBuildTimeAnalyzer(),
            ("src/Demo.AdvertsService/Startup.cs", source));

        Assert.DoesNotContain(diagnostics, d => d.Id == ObservabilityBuildTimeAnalyzer.MissingServiceDefaultsRule.Id);
    }

    [Fact]
    public async Task DoesNotFireWhenProgramNeverBuildsAHost()
    {
        const string source = """
            Console.WriteLine("Hello, world!");
            """;

        var diagnostics = await AnalyzerTestHelper.GetDiagnosticsAsync(
            new ObservabilityBuildTimeAnalyzer(),
            ("src/Demo.Tool/Program.cs", source));

        Assert.DoesNotContain(diagnostics, d => d.Id == ObservabilityBuildTimeAnalyzer.MissingServiceDefaultsRule.Id);
    }
}
