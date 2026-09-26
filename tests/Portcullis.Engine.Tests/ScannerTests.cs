using Portcullis.Engine;
using Portcullis.Rules;

namespace Portcullis.Engine.Tests;

public class ScannerTests
{
    [Fact]
    public async Task ScanAsync_WithNoViolatingSource_ReturnsValidEmptyResult()
    {
        // RuleRegistry.All was empty at M2 (the skeleton milestone); Track A has since
        // registered 3 rules (see RuleRegistryTests), so this asserts against
        // RuleRegistry.All.Length rather than the literal 0 the original M2 test
        // asserted. "Foo" itself triggers none of the 3 registered rules: it is not in
        // a kernel-named folder (P2), not a controller/DbContext/DbSet (P9), and has no
        // base type (P10).
        var tempDir = Directory.CreateTempSubdirectory("portcullis-scan-test-");
        try
        {
            await File.WriteAllTextAsync(
                Path.Combine(tempDir.FullName, "Sample.cs"),
                "namespace Sample;\n\npublic class Foo { }\n");

            var result = await Scanner.ScanAsync(tempDir.FullName);

            Assert.Equal(Scanner.SchemaVersion, result.SchemaVersion);
            Assert.Equal(1, result.FilesScanned);
            Assert.Equal(RuleRegistry.All.Length, result.RulesEvaluated);
            Assert.Empty(result.OfPrinciples());
            Assert.Equal(0, result.Summary.ErrorCount);
            Assert.Equal(0, result.PrincipleWarningCount());
            Assert.Equal(0, result.Summary.InfoCount);
        }
        finally
        {
            tempDir.Delete(recursive: true);
        }
    }

    [Fact]
    public async Task ScanAsync_ExcludesBinAndObjDirectories()
    {
        var tempDir = Directory.CreateTempSubdirectory("portcullis-scan-test-");
        try
        {
            Directory.CreateDirectory(Path.Combine(tempDir.FullName, "bin"));
            Directory.CreateDirectory(Path.Combine(tempDir.FullName, "obj"));
            await File.WriteAllTextAsync(Path.Combine(tempDir.FullName, "bin", "Generated.cs"), "class A {}");
            await File.WriteAllTextAsync(Path.Combine(tempDir.FullName, "obj", "Generated.cs"), "class B {}");
            await File.WriteAllTextAsync(Path.Combine(tempDir.FullName, "Real.cs"), "class C {}");

            var result = await Scanner.ScanAsync(tempDir.FullName);

            Assert.Equal(1, result.FilesScanned);
        }
        finally
        {
            tempDir.Delete(recursive: true);
        }
    }

    [Fact]
    public async Task ScanAsync_OnMissingPath_ReturnsEmptyResultRatherThanThrowing()
    {
        var result = await Scanner.ScanAsync(Path.Combine(Path.GetTempPath(), "portcullis-does-not-exist-" + Guid.NewGuid()));

        Assert.Equal(0, result.FilesScanned);
        Assert.Empty(result.Violations);
    }
}
