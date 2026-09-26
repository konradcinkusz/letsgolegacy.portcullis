using Portcullis.Engine.Provenance;

namespace Portcullis.Engine.Tests;

/// <summary>
/// Covers the M4 wiring of docs/SPEC.md section 4's consumption contract: a
/// <see cref="ProvenanceReport"/> passed into <see cref="Scanner.ScanAsync"/> escalates
/// the severity of any violation whose location falls inside an Ai-sourced range.
/// Exercised against the real, registered rules (<see cref="Rules.RuleRegistry.All"/>),
/// not a synthetic diagnostic, so this proves the end-to-end path a real scan takes.
/// </summary>
public class ScannerProvenanceTests
{
    // Triggers PORTCULLIS_P10_CUSTOM_BASE_CLASS (ExtensibilityInheritanceAnalyzer),
    // default severity "warning" — see RuleRegistryTests / ExtensibilityInheritanceAnalyzer.
    private const string SourceWithWarningViolation =
        "namespace Sample;\n\npublic class Base { }\npublic class Derived : Base { }\n";

    // Triggers PORTCULLIS_P9_CONTROLLER_NO_DBCONTEXT (ProgramManifestLayeringAnalyzer),
    // default severity "error" — see ProgramManifestLayeringAnalyzerTests for the same
    // minimal repro.
    private const string SourceWithErrorViolation = """
        namespace Demo;

        public class FooDbContext : DbContext
        {
        }

        [ApiController]
        public class FooController : ControllerBase
        {
            private readonly FooDbContext _db;

            public FooController(FooDbContext db)
            {
                _db = db;
            }
        }
        """;

    private static ProvenanceReport SingleAiRange(string filePath, int line, string source = "test") =>
        new("1.0.0", source, [
            new ProvenanceRange(
                filePath, line, line, ProvenanceSource.Ai, 0.9,
                new ProvenanceAttribution("claude-code", "commit-trailer", "deadbeef")),
        ]);

    [Fact]
    public async Task ScanAsync_WarningViolationInsideAiRange_EscalatesToError()
    {
        var tempDir = Directory.CreateTempSubdirectory("portcullis-provenance-test-");
        try
        {
            await File.WriteAllTextAsync(Path.Combine(tempDir.FullName, "Sample.cs"), SourceWithWarningViolation);

            var baseline = await Scanner.ScanAsync(tempDir.FullName);
            var baselineViolation = Assert.Single(baseline.OfPrinciples());
            Assert.Equal("warning", baselineViolation.Severity);

            var provenance = SingleAiRange(baselineViolation.FilePath, baselineViolation.Line);
            var result = await Scanner.ScanAsync(tempDir.FullName, provenance, tempDir.FullName);

            var violation = Assert.Single(result.OfPrinciples());
            Assert.Equal("error", violation.Severity);
            Assert.Equal(1, result.Summary.ErrorCount);
            Assert.Equal(0, result.PrincipleWarningCount());
        }
        finally
        {
            tempDir.Delete(recursive: true);
        }
    }

    [Fact]
    public async Task ScanAsync_ErrorViolationInsideAiRange_StaysError()
    {
        var tempDir = Directory.CreateTempSubdirectory("portcullis-provenance-test-");
        try
        {
            await File.WriteAllTextAsync(Path.Combine(tempDir.FullName, "Foo.cs"), SourceWithErrorViolation);

            var baseline = await Scanner.ScanAsync(tempDir.FullName);
            var baselineViolation = Assert.Single(
                baseline.Violations, v => v.RuleId == "PORTCULLIS_P9_CONTROLLER_NO_DBCONTEXT");
            Assert.Equal("error", baselineViolation.Severity);

            var provenance = SingleAiRange(baselineViolation.FilePath, baselineViolation.Line);
            var result = await Scanner.ScanAsync(tempDir.FullName, provenance, tempDir.FullName);

            var violation = Assert.Single(
                result.Violations, v => v.RuleId == "PORTCULLIS_P9_CONTROLLER_NO_DBCONTEXT");
            Assert.Equal("error", violation.Severity);
        }
        finally
        {
            tempDir.Delete(recursive: true);
        }
    }

    [Fact]
    public async Task ScanAsync_ProvenanceRangeOnADifferentLine_DoesNotEscalate()
    {
        var tempDir = Directory.CreateTempSubdirectory("portcullis-provenance-test-");
        try
        {
            await File.WriteAllTextAsync(Path.Combine(tempDir.FullName, "Sample.cs"), SourceWithWarningViolation);

            var baseline = await Scanner.ScanAsync(tempDir.FullName);
            var baselineViolation = Assert.Single(baseline.OfPrinciples());

            var provenance = SingleAiRange(baselineViolation.FilePath, baselineViolation.Line + 100);
            var result = await Scanner.ScanAsync(tempDir.FullName, provenance, tempDir.FullName);

            var violation = Assert.Single(result.OfPrinciples());
            Assert.Equal("warning", violation.Severity);
        }
        finally
        {
            tempDir.Delete(recursive: true);
        }
    }

    [Fact]
    public async Task ScanAsync_HumanSourcedRange_DoesNotEscalate()
    {
        var tempDir = Directory.CreateTempSubdirectory("portcullis-provenance-test-");
        try
        {
            await File.WriteAllTextAsync(Path.Combine(tempDir.FullName, "Sample.cs"), SourceWithWarningViolation);

            var baseline = await Scanner.ScanAsync(tempDir.FullName);
            var baselineViolation = Assert.Single(baseline.OfPrinciples());

            var provenance = new ProvenanceReport("1.0.0", "test", [
                new ProvenanceRange(
                    baselineViolation.FilePath, baselineViolation.Line, baselineViolation.Line,
                    ProvenanceSource.Human, 0.7, new ProvenanceAttribution(null, "git-blame-authorship", "deadbeef")),
            ]);
            var result = await Scanner.ScanAsync(tempDir.FullName, provenance, tempDir.FullName);

            var violation = Assert.Single(result.OfPrinciples());
            Assert.Equal("warning", violation.Severity);
        }
        finally
        {
            tempDir.Delete(recursive: true);
        }
    }

    [Fact]
    public async Task ScanAsync_MixedSourcedRange_DoesNotEscalate()
    {
        // Policy call recorded in Scanner.ApplyProvenanceEscalation: a human co-author on
        // the commit is treated the same as review, not as unattended AI output.
        var tempDir = Directory.CreateTempSubdirectory("portcullis-provenance-test-");
        try
        {
            await File.WriteAllTextAsync(Path.Combine(tempDir.FullName, "Sample.cs"), SourceWithWarningViolation);

            var baseline = await Scanner.ScanAsync(tempDir.FullName);
            var baselineViolation = Assert.Single(baseline.OfPrinciples());

            var provenance = new ProvenanceReport("1.0.0", "test", [
                new ProvenanceRange(
                    baselineViolation.FilePath, baselineViolation.Line, baselineViolation.Line,
                    ProvenanceSource.Mixed, 0.7, new ProvenanceAttribution("claude-code", "commit-trailer", "deadbeef")),
            ]);
            var result = await Scanner.ScanAsync(tempDir.FullName, provenance, tempDir.FullName);

            var violation = Assert.Single(result.OfPrinciples());
            Assert.Equal("warning", violation.Severity);
        }
        finally
        {
            tempDir.Delete(recursive: true);
        }
    }

    [Fact]
    public async Task ScanAsync_NoProvenanceGiven_LeavesSeverityUnchanged()
    {
        var tempDir = Directory.CreateTempSubdirectory("portcullis-provenance-test-");
        try
        {
            await File.WriteAllTextAsync(Path.Combine(tempDir.FullName, "Sample.cs"), SourceWithWarningViolation);

            var result = await Scanner.ScanAsync(tempDir.FullName, provenance: null);

            var violation = Assert.Single(result.OfPrinciples());
            Assert.Equal("warning", violation.Severity);
        }
        finally
        {
            tempDir.Delete(recursive: true);
        }
    }

    [Fact]
    public async Task ScanAsync_ProvenanceRootOmitted_DefaultsToScannedPath()
    {
        var tempDir = Directory.CreateTempSubdirectory("portcullis-provenance-test-");
        try
        {
            await File.WriteAllTextAsync(Path.Combine(tempDir.FullName, "Sample.cs"), SourceWithWarningViolation);

            var baseline = await Scanner.ScanAsync(tempDir.FullName);
            var baselineViolation = Assert.Single(baseline.OfPrinciples());

            var provenance = SingleAiRange(baselineViolation.FilePath, baselineViolation.Line);
            // provenanceRoot intentionally omitted here.
            var result = await Scanner.ScanAsync(tempDir.FullName, provenance);

            var violation = Assert.Single(result.OfPrinciples());
            Assert.Equal("error", violation.Severity);
        }
        finally
        {
            tempDir.Delete(recursive: true);
        }
    }

    [Fact]
    public async Task ScanAsync_ScannedPathIsSubdirectoryOfProvenanceRoot_StillMatchesCorrectly()
    {
        // The exact shape Track B's PR-check workflow needs: `portcullis scan
        // <repo>/src` while provenance is computed for the whole <repo>, whose
        // ProvenanceRange.FilePath values (git diff/blame output) are relative to the
        // repository's top-level directory, not to the scanned subdirectory.
        var repoRoot = Directory.CreateTempSubdirectory("portcullis-provenance-repo-");
        try
        {
            var srcDir = Directory.CreateDirectory(Path.Combine(repoRoot.FullName, "src"));
            await File.WriteAllTextAsync(Path.Combine(srcDir.FullName, "Sample.cs"), SourceWithWarningViolation);

            var baseline = await Scanner.ScanAsync(srcDir.FullName);
            var baselineViolation = Assert.Single(baseline.OfPrinciples());
            Assert.Equal("Sample.cs", baselineViolation.FilePath);

            var provenance = SingleAiRange("src/Sample.cs", baselineViolation.Line);
            var result = await Scanner.ScanAsync(srcDir.FullName, provenance, repoRoot.FullName);

            var violation = Assert.Single(result.OfPrinciples());
            Assert.Equal("error", violation.Severity);
        }
        finally
        {
            repoRoot.Delete(recursive: true);
        }
    }
}
