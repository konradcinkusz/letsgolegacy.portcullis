using Microsoft.CodeAnalysis;
using Portcullis.Rules;

namespace Portcullis.Engine.Tests.Rules;

/// <summary>
/// PORTCULLIS_MIG_CONFIGURATION_MANAGER. The .NET Framework configuration entry points fire
/// however they are reached; the modern <c>Microsoft.Extensions.Configuration
/// .ConfigurationManager</c> — the exact look-alike a text search would flag — does not.
/// </summary>
public class ConfigurationManagerAnalyzerTests
{
    private static readonly string RuleId = ConfigurationManagerAnalyzer.ConfigurationManagerRule.Id;

    private static async Task<List<Diagnostic>> RunAsync(string source, string path = "src/Shop/Settings.cs")
    {
        var diagnostics = await AnalyzerTestHelper.GetDiagnosticsAsync(new ConfigurationManagerAnalyzer(), (path, source));
        // Concurrent execution reports in no fixed order; tests read them in source order.
        return diagnostics.Where(d => d.Id == RuleId).OrderBy(d => d.Location.SourceSpan.Start).ToList();
    }

    private static string Located(Diagnostic diagnostic) =>
        diagnostic.Location.SourceTree!.GetText().ToString(diagnostic.Location.SourceSpan);

    [Fact]
    public async Task FiresOnAppSettings()
    {
        var hits = await RunAsync("""
            using System.Configuration;

            namespace Shop;

            public static class Settings
            {
                public static string Currency() => ConfigurationManager.AppSettings["Currency"];
            }
            """);

        var hit = Assert.Single(hits);
        Assert.Equal("ConfigurationManager.AppSettings", Located(hit));
        Assert.Contains("System.Configuration.ConfigurationManager", hit.GetMessage());
        Assert.Equal(DiagnosticSeverity.Error, hit.Severity);
    }

    [Theory]
    [InlineData("System.Configuration.ConfigurationManager.ConnectionStrings[\"Shop\"].ConnectionString", "System.Configuration.ConfigurationManager.ConnectionStrings")]
    [InlineData("System.Configuration.ConfigurationManager.GetSection(\"shop\")", "System.Configuration.ConfigurationManager.GetSection(\"shop\")")]
    [InlineData("System.Web.Configuration.WebConfigurationManager.AppSettings[\"Currency\"]", "System.Web.Configuration.WebConfigurationManager.AppSettings")]
    [InlineData("System.Web.Configuration.WebConfigurationManager.GetSection(\"shop\")", "System.Web.Configuration.WebConfigurationManager.GetSection(\"shop\")")]
    [InlineData("System.Configuration.ConfigurationSettings.AppSettings[\"Currency\"]", "System.Configuration.ConfigurationSettings.AppSettings")]
    [InlineData("System.Configuration.ConfigurationManager.OpenExeConfiguration(System.Configuration.ConfigurationUserLevel.None).FilePath", "System.Configuration.ConfigurationManager.OpenExeConfiguration(System.Configuration.ConfigurationUserLevel.None)")]
    public async Task FiresOnEveryLegacyEntryPoint(string expression, string expectedLocation)
    {
        var hits = await RunAsync($$"""
            namespace Shop;

            public static class Settings
            {
                public static object Read() => {{expression}};
            }
            """);

        Assert.Equal(expectedLocation, Located(Assert.Single(hits)));
    }

    [Fact]
    public async Task FiresOnUsingStaticAndOnAMethodGroup()
    {
        var hits = await RunAsync("""
            using System;
            using static System.Configuration.ConfigurationManager;

            namespace Shop;

            public static class Settings
            {
                public static string Currency() => AppSettings["Currency"];
                public static Func<string, object> Reader() => GetSection;
            }
            """);

        Assert.Equal(new[] { "AppSettings", "GetSection" }, hits.Select(Located).ToArray());
    }

    [Fact]
    public async Task DoesNotFireOnTheModernConfigurationManager()
    {
        // Microsoft.Extensions.Configuration.ConfigurationManager is what
        // WebApplicationBuilder.Configuration returns: same simple name, the modern API.
        const string modern = """
            namespace Microsoft.Extensions.Configuration
            {
                public interface IConfiguration
                {
                    string this[string key] { get; }
                }

                public sealed class ConfigurationManager : IConfiguration
                {
                    public string this[string key] => null;
                    public static ConfigurationManager Create() => new ConfigurationManager();
                }
            }
            """;
        const string consumer = """
            using Microsoft.Extensions.Configuration;

            namespace Shop;

            public static class Settings
            {
                public static string Currency(IConfiguration configuration) => configuration["Currency"];
                public static string Fresh() => ConfigurationManager.Create()["Currency"];
            }
            """;

        var diagnostics = await AnalyzerTestHelper.GetDiagnosticsAsync(
            new ConfigurationManagerAnalyzer(), ("src/Stubs/Configuration.cs", modern), ("src/Shop/Settings.cs", consumer));

        Assert.DoesNotContain(diagnostics, d => d.Id == RuleId);
    }

    [Fact]
    public async Task DoesNotFireOnATeamsOwnConfigurationManager()
    {
        var hits = await RunAsync("""
            namespace Shop.Infrastructure
            {
                public static class ConfigurationManager
                {
                    public static string AppSettings(string key) => key;
                }

                public static class Settings
                {
                    public static string Currency() => ConfigurationManager.AppSettings("Currency");
                }
            }
            """);

        Assert.Empty(hits);
    }

    [Fact]
    public async Task DoesNotFireOnNameofOrTypeof()
    {
        // Neither reads a setting: nameof takes a name, typeof a type.
        var hits = await RunAsync("""
            using System;
            using System.Configuration;

            namespace Shop;

            public static class Names
            {
                public const string Property = nameof(ConfigurationManager.AppSettings);
                public static Type Type() => typeof(ConfigurationManager);
            }
            """);

        Assert.Empty(hits);
    }

    [Fact]
    public async Task DoesNotFireInAConfiguredMigrationExemptFolder()
    {
        const string source = """
            using System.Configuration;

            namespace Shop;

            public static class Settings
            {
                public static string Currency() => ConfigurationManager.AppSettings["Currency"];
            }
            """;
        var configuration = new Dictionary<string, string>
        {
            [PortcullisConventionKeys.MigrationExemptFolders] = "Legacy, Compat",
        };

        var diagnostics = await AnalyzerTestHelper.GetDiagnosticsAsync(
            new ConfigurationManagerAnalyzer(), configuration,
            ("src/Compat/Settings.cs", source), ("src/Shop/Settings.cs", source));

        var hit = Assert.Single(diagnostics, d => d.Id == RuleId);
        Assert.Equal("src/Shop/Settings.cs", hit.Location.SourceTree!.FilePath);
    }

    [Fact]
    public async Task ReportsTheFullMessage()
    {
        var hits = await RunAsync("""
            using System.Configuration;

            namespace Shop;

            public static class Settings
            {
                public static string Currency() => ConfigurationManager.AppSettings["Currency"];
            }
            """);

        Assert.Equal(
            "'ConfigurationManager.AppSettings' reads settings through System.Configuration.ConfigurationManager. " +
            "On modern .NET that API reads <app>.dll.config — not web.config, never appsettings.json — so a value " +
            "that used to arrive can come back null at run time with no build error. Bind IOptions<T> or read " +
            "IConfiguration instead.",
            Assert.Single(hits).GetMessage());
    }

    [Fact]
    public async Task DoesNotFireInGeneratedCode()
    {
        var hits = await RunAsync("""
            // <auto-generated />
            namespace Shop;

            public static class Designer
            {
                public static string Currency() => System.Configuration.ConfigurationManager.AppSettings["Currency"];
            }
            """);

        Assert.Empty(hits);
    }

    [Fact]
    public async Task RecognisesConfigurationManagerDeclaredByAnyAssembly()
    {
        const string systemConfiguration = """
            namespace System.Configuration
            {
                public static class ConfigurationManager
                {
                    public static object GetSection(string sectionName) => null;
                }
            }
            """;
        const string consumer = """
            namespace Shop;

            public static class Settings
            {
                public static object Section() => System.Configuration.ConfigurationManager.GetSection("shop");
            }
            """;

        var diagnostics = await AnalyzerTestHelper.GetDiagnosticsAgainstCoreLibraryOnlyAsync(
            new ConfigurationManagerAnalyzer(),
            ("src/Compat/Configuration.cs", systemConfiguration), ("src/Shop/Settings.cs", consumer));

        Assert.Single(diagnostics, d => d.Id == RuleId);
    }
}
