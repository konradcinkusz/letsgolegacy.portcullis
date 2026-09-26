using Portcullis.Engine.Configuration;
using Portcullis.Rules;

namespace Portcullis.Engine.Tests;

/// <summary>
/// The migration rules through the CLI's own path — Scanner.ScanAsync over files on disk,
/// with portcullis.json — rather than through an analyzer test harness. This is the proof
/// that the channel the pull-request gate runs in binds the legacy names at all: the scan
/// has no project build, so without the framework references and the declared legacy
/// surface (Portcullis.Engine.Semantics.ScanReferences) these rules would report nothing.
/// </summary>
public class ScannerMigrationTests
{
    private const string LegacyService = """
        using System.Configuration;
        using System.Net.Http;
        using System.Threading.Tasks;
        using System.Web;
        using System.Web.Mvc;

        namespace Shop;

        public class OrdersService
        {
            private readonly HttpClient _client = new HttpClient();

            public string Currency() => ConfigurationManager.AppSettings["Currency"];

            public string User() => HttpContext.Current.User.Identity.Name;

            public string Rates() => _client.GetStringAsync("https://example.test/rates").Result;

            public string Encode(string value) => HttpUtility.UrlEncode(value);

            public async Task<int> CountAsync(Task<int> pending) => await pending;
        }
        """;

    [Fact]
    public async Task ScanAsync_LegacyCode_ReportsEachMigrationRuleOnItsLineWithItsDefaultSeverity()
    {
        var dir = Directory.CreateTempSubdirectory("portcullis-migration-scan-");
        try
        {
            await File.WriteAllTextAsync(Path.Combine(dir.FullName, "OrdersService.cs"), LegacyService);

            var result = await Scanner.ScanAsync(dir.FullName);

            var migration = result.Violations
                .Where(v => v.RuleId.StartsWith("PORTCULLIS_MIG_", StringComparison.Ordinal))
                .Select(v => (v.RuleId, v.Line, v.Severity))
                .ToArray();

            // `using System.Web;` (line 4) and HttpUtility (line 19) are modern .NET and stay
            // silent, as does the awaited task on line 21.
            Assert.Equal(
                new[]
                {
                    (SystemWebUsageAnalyzer.SystemWebRule.Id, 5, "warning"),                  // using System.Web.Mvc;
                    (ConfigurationManagerAnalyzer.ConfigurationManagerRule.Id, 13, "error"), // ConfigurationManager.AppSettings
                    (HttpContextCurrentAnalyzer.HttpContextCurrentRule.Id, 15, "error"),      // HttpContext.Current
                    (SystemWebUsageAnalyzer.SystemWebRule.Id, 15, "warning"),                 // HttpContext
                    (SyncOverAsyncAnalyzer.SyncOverAsyncRule.Id, 17, "warning"),              // .Result
                },
                migration.OrderBy(m => m.Line).ThenBy(m => m.RuleId, StringComparer.Ordinal).ToArray());
            Assert.True(result.Gate!.Blocked);
        }
        finally
        {
            dir.Delete(recursive: true);
        }
    }

    [Fact]
    public async Task ScanAsync_MigrationExemptFolderInPortcullisJson_SilencesTheMigrationRulesThereOnly()
    {
        var dir = Directory.CreateTempSubdirectory("portcullis-migration-exempt-");
        try
        {
            var compat = Directory.CreateDirectory(Path.Combine(dir.FullName, "Compat"));
            var shop = Directory.CreateDirectory(Path.Combine(dir.FullName, "Shop"));
            await File.WriteAllTextAsync(Path.Combine(compat.FullName, "OrdersService.cs"), LegacyService);
            await File.WriteAllTextAsync(Path.Combine(shop.FullName, "OrdersService.cs"), LegacyService.Replace("namespace Shop;", "namespace Shop.Current;"));
            await File.WriteAllTextAsync(
                Path.Combine(dir.FullName, PortcullisConfigFile.FileName), """{ "migrationExemptFolders": ["Compat"] }""");

            var result = await Scanner.ScanAsync(dir.FullName);

            var files = result.Violations
                .Where(v => v.RuleId.StartsWith("PORTCULLIS_MIG_", StringComparison.Ordinal))
                .Select(v => v.FilePath)
                .Distinct()
                .ToArray();
            Assert.Equal(new[] { "Shop/OrdersService.cs" }, files);
        }
        finally
        {
            dir.Delete(recursive: true);
        }
    }
}
