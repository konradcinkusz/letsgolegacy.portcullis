using Portcullis.Rules;

namespace Portcullis.Engine.Tests.Rules;

public class KernelBoundaryAnalyzerTests
{
    private static string Padding(int lines) =>
        string.Join('\n', Enumerable.Range(0, lines).Select(i => $"// padding line {i}"));

    [Fact]
    public async Task LocCeiling_FiresWhenKernelFolderExceeds800Lines()
    {
        var diagnostics = await AnalyzerTestHelper.GetDiagnosticsAsync(
            new KernelBoundaryAnalyzer(),
            ("src/Demo.ServiceDefaults/FileA.cs", "namespace Demo.ServiceDefaults;\n" + Padding(500)),
            ("src/Demo.ServiceDefaults/FileB.cs", "namespace Demo.ServiceDefaults;\n" + Padding(400)));

        var hit = Assert.Single(diagnostics, d => d.Id == KernelBoundaryAnalyzer.LocCeilingRule.Id);
        Assert.Equal("src/Demo.ServiceDefaults/FileA.cs", hit.Location.SourceTree!.FilePath);
    }

    [Fact]
    public async Task LocCeiling_DoesNotFireWhenKernelFolderIsUnderCeiling()
    {
        var diagnostics = await AnalyzerTestHelper.GetDiagnosticsAsync(
            new KernelBoundaryAnalyzer(),
            ("src/Demo.ServiceDefaults/FileA.cs", "namespace Demo.ServiceDefaults;\n" + Padding(100)));

        Assert.DoesNotContain(diagnostics, d => d.Id == KernelBoundaryAnalyzer.LocCeilingRule.Id);
    }

    [Fact]
    public async Task LocCeiling_IgnoresFilesOutsideAKernelFolder()
    {
        var diagnostics = await AnalyzerTestHelper.GetDiagnosticsAsync(
            new KernelBoundaryAnalyzer(),
            ("src/Demo.AdvertsService/BigFile.cs", "namespace Demo.AdvertsService;\n" + Padding(900)));

        Assert.DoesNotContain(diagnostics, d => d.Id == KernelBoundaryAnalyzer.LocCeilingRule.Id);
    }

    [Fact]
    public async Task EntityReference_FiresWhenKernelFileReferencesADomainType()
    {
        const string kernelSource = """
            namespace Demo.ServiceDefaults;

            using Demo.AdvertsService.Domain;

            public static class Extensions
            {
                public static Advert BuildDefault() => new Advert();
            }
            """;
        const string domainSource = """
            namespace Demo.AdvertsService.Domain;

            public class Advert
            {
            }
            """;

        var diagnostics = await AnalyzerTestHelper.GetDiagnosticsAsync(
            new KernelBoundaryAnalyzer(),
            ("src/Demo.ServiceDefaults/Extensions.cs", kernelSource),
            ("src/Demo.AdvertsService/Domain/Advert.cs", domainSource));

        var hit = Assert.Single(diagnostics, d => d.Id == KernelBoundaryAnalyzer.KernelEntityReferenceRule.Id);
        Assert.Contains("Advert", hit.GetMessage());
    }

    [Fact]
    public async Task EntityReference_DoesNotFireWhenKernelFileReferencesNoDomainType()
    {
        const string kernelSource = """
            namespace Demo.ServiceDefaults;

            public static class Extensions
            {
                public static int AddOne(int x) => x + 1;
            }
            """;

        var diagnostics = await AnalyzerTestHelper.GetDiagnosticsAsync(
            new KernelBoundaryAnalyzer(),
            ("src/Demo.ServiceDefaults/Extensions.cs", kernelSource));

        Assert.DoesNotContain(diagnostics, d => d.Id == KernelBoundaryAnalyzer.KernelEntityReferenceRule.Id);
    }
}
