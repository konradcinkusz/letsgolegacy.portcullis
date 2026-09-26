using Portcullis.Engine.Configuration;
using Portcullis.Rules;

namespace Portcullis.Engine.Tests.Configuration;

/// <summary>
/// Reading portcullis.json and flattening it onto the same keys a .globalconfig would
/// supply. The point of these tests is that both distribution channels reach the identical
/// convention model — so they assert the flattened key/value shape, not just that a file
/// parsed.
/// </summary>
public class PortcullisConfigFileTests
{
    // PortcullisConventions.From builds a fresh instance whenever the host supplies a
    // provider at all, so "fell back to the defaults" is a statement about values, never
    // about reference identity with the Default singleton.
    private static void AssertMatchesDefaults(PortcullisConventions conventions)
    {
        var defaults = PortcullisConventions.Default;
        Assert.Equal(defaults.KernelFolders.ToArray(), conventions.KernelFolders.ToArray());
        Assert.Equal(defaults.EntityFolders.ToArray(), conventions.EntityFolders.ToArray());
        Assert.Equal(defaults.AdapterFolders.ToArray(), conventions.AdapterFolders.ToArray());
        Assert.Equal(defaults.VendorNamespaces.ToArray(), conventions.VendorNamespaces.ToArray());
        Assert.Equal(defaults.EntryPointFileNames.ToArray(), conventions.EntryPointFileNames.ToArray());
        Assert.Equal(defaults.KernelLineCeiling, conventions.KernelLineCeiling);
        Assert.Equal(defaults.MigrationExemptFolders.ToArray(), conventions.MigrationExemptFolders.ToArray());
        Assert.Equal(defaults.SystemWebAllowedTypes.ToArray(), conventions.SystemWebAllowedTypes.ToArray());
        Assert.Empty(conventions.ExplicitlyConfiguredKeys);
    }

    private static string WriteConfig(DirectoryInfo directory, string json)
    {
        var path = Path.Combine(directory.FullName, PortcullisConfigFile.FileName);
        File.WriteAllText(path, json);
        return path;
    }

    [Fact]
    public void Load_NoConfigFile_ReturnsNoValuesAndNoError()
    {
        var dir = Directory.CreateTempSubdirectory("portcullis-config-none-");
        try
        {
            var config = PortcullisConfigFile.Load(dir.FullName);

            Assert.Empty(config.Values);
            Assert.Null(config.Path);
            Assert.Null(config.Error);
            // The whole point: no config is not an error, it is the default state.
            AssertMatchesDefaults(PortcullisConventions.From(config.ToAnalyzerOptions()));
        }
        finally
        {
            dir.Delete(recursive: true);
        }
    }

    [Fact]
    public void Load_FullConfig_FlattensEveryPropertyOntoTheSharedKeys()
    {
        var dir = Directory.CreateTempSubdirectory("portcullis-config-full-");
        try
        {
            WriteConfig(dir, """
                {
                  "kernelFolders": ["Common", "Shared"],
                  "entityFolders": ["Model"],
                  "adapterFolders": ["External"],
                  "vendorNamespaces": ["Acme"],
                  "entryPointFileNames": ["Startup.cs"],
                  "kernelLineCeiling": 400,
                  "migrationExemptFolders": ["Legacy", "Compat"],
                  "systemWebAllowedTypes": ["System.Web.HttpUtility"]
                }
                """);

            var config = PortcullisConfigFile.Load(dir.FullName);

            Assert.Null(config.Error);
            Assert.Equal("Common,Shared", config.Values[PortcullisConventionKeys.KernelFolders]);
            Assert.Equal("Model", config.Values[PortcullisConventionKeys.EntityFolders]);
            Assert.Equal("External", config.Values[PortcullisConventionKeys.AdapterFolders]);
            Assert.Equal("Acme", config.Values[PortcullisConventionKeys.VendorNamespaces]);
            Assert.Equal("Startup.cs", config.Values[PortcullisConventionKeys.EntryPointFileNames]);
            Assert.Equal("400", config.Values[PortcullisConventionKeys.KernelLineCeiling]);
            Assert.Equal("Legacy,Compat", config.Values[PortcullisConventionKeys.MigrationExemptFolders]);
            Assert.Equal("System.Web.HttpUtility", config.Values[PortcullisConventionKeys.SystemWebAllowedTypes]);

            var conventions = PortcullisConventions.From(config.ToAnalyzerOptions());
            Assert.Equal(["Common", "Shared"], conventions.KernelFolders.ToArray());
            Assert.Equal(["Startup.cs"], conventions.EntryPointFileNames.ToArray());
            Assert.Equal(400, conventions.KernelLineCeiling);
            Assert.Equal(["Legacy", "Compat"], conventions.MigrationExemptFolders.ToArray());
            Assert.Equal(["System.Web.HttpUtility"], conventions.SystemWebAllowedTypes.ToArray());
        }
        finally
        {
            dir.Delete(recursive: true);
        }
    }

    [Fact]
    public void Load_PartialConfig_LeavesUnmentionedConventionsAtTheirDefaults()
    {
        // Per-key fallback, not all-or-nothing: naming one convention must not silently
        // blank the other five.
        var dir = Directory.CreateTempSubdirectory("portcullis-config-partial-");
        try
        {
            WriteConfig(dir, """{ "kernelFolders": ["Common"] }""");

            var conventions = PortcullisConventions.From(PortcullisConfigFile.Load(dir.FullName).ToAnalyzerOptions());

            Assert.Equal(["Common"], conventions.KernelFolders.ToArray());
            Assert.Equal(PortcullisConventions.Default.EntityFolders.ToArray(), conventions.EntityFolders.ToArray());
            Assert.Equal(PortcullisConventions.Default.AdapterFolders.ToArray(), conventions.AdapterFolders.ToArray());
            Assert.Equal(PortcullisConventions.Default.KernelLineCeiling, conventions.KernelLineCeiling);
            Assert.True(conventions.IsExplicitlyConfigured(PortcullisConventionKeys.KernelFolders));
            Assert.False(conventions.IsExplicitlyConfigured(PortcullisConventionKeys.EntityFolders));
        }
        finally
        {
            dir.Delete(recursive: true);
        }
    }

    [Fact]
    public void Load_ExplicitlyEmptyList_IsHonouredRatherThanTreatedAsUnset()
    {
        // "This project has no shared kernel" must be declarable. If an empty list fell
        // back to the defaults there would be no way to switch a convention off.
        var dir = Directory.CreateTempSubdirectory("portcullis-config-empty-");
        try
        {
            WriteConfig(dir, """{ "kernelFolders": [] }""");

            var conventions = PortcullisConventions.From(PortcullisConfigFile.Load(dir.FullName).ToAnalyzerOptions());

            Assert.Empty(conventions.KernelFolders);
            Assert.True(conventions.IsExplicitlyConfigured(PortcullisConventionKeys.KernelFolders));
        }
        finally
        {
            dir.Delete(recursive: true);
        }
    }

    [Fact]
    public void Load_MalformedJson_ReportsAnErrorAndFallsBackToDefaults()
    {
        // A broken config must not take the gate down — but it must not be silent either,
        // or a team runs indefinitely on defaults believing their config is in force.
        var dir = Directory.CreateTempSubdirectory("portcullis-config-broken-");
        try
        {
            WriteConfig(dir, "{ this is not json");

            var config = PortcullisConfigFile.Load(dir.FullName);

            Assert.NotNull(config.Error);
            Assert.NotNull(config.Path);
            Assert.Empty(config.Values);
            AssertMatchesDefaults(PortcullisConventions.From(config.ToAnalyzerOptions()));
        }
        finally
        {
            dir.Delete(recursive: true);
        }
    }

    [Fact]
    public void Load_UnknownProperty_IsIgnoredRatherThanRejected()
    {
        // Forward compatibility: a config written for a newer portcullis still works.
        var dir = Directory.CreateTempSubdirectory("portcullis-config-unknown-");
        try
        {
            WriteConfig(dir, """{ "kernelFolders": ["Common"], "somethingFromTheFuture": 42 }""");

            var config = PortcullisConfigFile.Load(dir.FullName);

            Assert.Null(config.Error);
            Assert.Equal("Common", config.Values[PortcullisConventionKeys.KernelFolders]);
        }
        finally
        {
            dir.Delete(recursive: true);
        }
    }

    [Fact]
    public async Task ScanAsync_ConfigNamingADifferentKernelFolder_MakesTheKernelRuleFireWhereItWasSilent()
    {
        // The end-to-end claim the whole configuration system exists to make true: a team
        // whose shared kernel is called "Common" gets it enforced instead of ignored.
        var dir = Directory.CreateTempSubdirectory("portcullis-config-e2e-");
        try
        {
            var kernel = Directory.CreateDirectory(Path.Combine(dir.FullName, "Common"));
            var padding = string.Join("\n", Enumerable.Range(1, 900).Select(i => $"    public const int N{i} = {i};"));
            await File.WriteAllTextAsync(
                Path.Combine(kernel.FullName, "Big.cs"),
                $"namespace Common;\n\npublic static class Big\n{{\n{padding}\n}}\n");

            var before = await Scanner.ScanAsync(dir.FullName);
            Assert.DoesNotContain(before.Violations, v => v.RuleId == KernelBoundaryAnalyzer.LocCeilingRule.Id);

            WriteConfig(dir, """{ "kernelFolders": ["Common"] }""");
            var after = await Scanner.ScanAsync(dir.FullName);

            Assert.Contains(after.Violations, v => v.RuleId == KernelBoundaryAnalyzer.LocCeilingRule.Id);
            Assert.True(after.Gate!.Blocked);
            Assert.Equal(Path.Combine(dir.FullName, PortcullisConfigFile.FileName), after.Configuration!.Path);
        }
        finally
        {
            dir.Delete(recursive: true);
        }
    }

    [Fact]
    public async Task ScanAsync_MalformedConfig_SurfacesTheErrorOnTheScanResult()
    {
        var dir = Directory.CreateTempSubdirectory("portcullis-config-e2e-broken-");
        try
        {
            await File.WriteAllTextAsync(Path.Combine(dir.FullName, "Sample.cs"), "namespace S;\n\npublic class F { }\n");
            WriteConfig(dir, "{ nope");

            var result = await Scanner.ScanAsync(dir.FullName);

            Assert.NotNull(result.Configuration);
            Assert.NotNull(result.Configuration.Error);
        }
        finally
        {
            dir.Delete(recursive: true);
        }
    }
}
