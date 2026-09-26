using Portcullis.Rules;

namespace Portcullis.Engine.Tests.Rules;

public class AntiCorruptionEdgeAnalyzerTests
{
    [Fact]
    public async Task FiresWhenApplicationCodeImportsAVendorSdkNamespace()
    {
        // The real, current shape of the reference consumer app's Consumer.AgentService.Application —
        // Anthropic.Models.Messages used directly outside Infrastructure/Claude/.
        const string source = """
            using Anthropic.Models.Messages;

            namespace Demo.AgentService.Application;

            public class AgentOrchestrator
            {
                public MessageParam? Build() => null;
            }
            """;

        var diagnostics = await AnalyzerTestHelper.GetDiagnosticsAsync(
            new AntiCorruptionEdgeAnalyzer(),
            ("src/Demo.AgentService/Application/AgentOrchestrator.cs", source));

        var hit = Assert.Single(diagnostics, d => d.Id == AntiCorruptionEdgeAnalyzer.VendorSdkOutsideAdapterRule.Id);
        Assert.Contains("Anthropic.Models.Messages", hit.GetMessage());
    }

    [Fact]
    public async Task DoesNotFireWhenTheSameImportLivesInAnInfrastructureFolder()
    {
        const string source = """
            using Anthropic.Models.Messages;

            namespace Demo.AgentService.Infrastructure.Claude;

            public class ClaudeClient
            {
                public MessageParam? Build() => null;
            }
            """;

        var diagnostics = await AnalyzerTestHelper.GetDiagnosticsAsync(
            new AntiCorruptionEdgeAnalyzer(),
            ("src/Demo.AgentService/Infrastructure/Claude/ClaudeClient.cs", source));

        Assert.DoesNotContain(diagnostics, d => d.Id == AntiCorruptionEdgeAnalyzer.VendorSdkOutsideAdapterRule.Id);
    }

    [Fact]
    public async Task DoesNotFireForANonVendorNamespace()
    {
        const string source = """
            using Microsoft.Extensions.Logging;

            namespace Demo.AgentService.Application;

            public class AgentOrchestrator
            {
            }
            """;

        var diagnostics = await AnalyzerTestHelper.GetDiagnosticsAsync(
            new AntiCorruptionEdgeAnalyzer(),
            ("src/Demo.AgentService/Application/AgentOrchestrator.cs", source));

        Assert.DoesNotContain(diagnostics, d => d.Id == AntiCorruptionEdgeAnalyzer.VendorSdkOutsideAdapterRule.Id);
    }

    [Fact]
    public async Task DoesNotFireWhenTheVendorImportIsInProgramCsForDiRegistration()
    {
        // The real, current shape of the reference consumer app's Consumer.AgentService/Program.cs: a
        // composition-root DI registration constructing the vendor client directly.
        const string source = """
            using Anthropic;

            var builder = WebApplication.CreateBuilder(args);

            builder.Services.AddSingleton(sp => new AnthropicClient());

            var app = builder.Build();
            app.Run();
            """;

        var diagnostics = await AnalyzerTestHelper.GetDiagnosticsAsync(
            new AntiCorruptionEdgeAnalyzer(),
            ("src/Demo.AgentService/Program.cs", source));

        Assert.DoesNotContain(diagnostics, d => d.Id == AntiCorruptionEdgeAnalyzer.VendorSdkOutsideAdapterRule.Id);
    }

    [Fact]
    public async Task VendorSdkOutsideAdapter_FiresForAMultiSegmentVendorNamespace()
    {
        // "Google.Cloud" shipped in the vendor list from the start but could never match:
        // the check compared only the ROOT segment of the import, and the root of
        // "Google.Cloud.Storage.V1" is "Google". The entry read as coverage and provided
        // none. This is the regression test for that fix.
        var diagnostics = await AnalyzerTestHelper.GetDiagnosticsAsync(
            new AntiCorruptionEdgeAnalyzer(),
            ("src/Demo.Api/Application/Uploads.cs",
                "using Google.Cloud.Storage.V1;\n\nnamespace Demo.Api.Application;\n\npublic class Uploads { }\n"));

        var hit = Assert.Single(diagnostics, d => d.Id == AntiCorruptionEdgeAnalyzer.VendorSdkOutsideAdapterRule.Id);
        Assert.Contains("Google.Cloud.Storage.V1", hit.GetMessage());
    }

    [Fact]
    public async Task VendorSdkOutsideAdapter_DoesNotFireForANamespaceThatMerelySharesAPrefix()
    {
        // The fix must match on a dotted-segment boundary, not on raw StartsWith, or
        // "Amazon" would flag an unrelated "AmazonStyleRecommender" of the team's own.
        var diagnostics = await AnalyzerTestHelper.GetDiagnosticsAsync(
            new AntiCorruptionEdgeAnalyzer(),
            ("src/Demo.Api/Application/Recs.cs",
                "using AmazonStyleRecommender.Core;\n\nnamespace Demo.Api.Application;\n\npublic class Recs { }\n"));

        Assert.DoesNotContain(diagnostics, d => d.Id == AntiCorruptionEdgeAnalyzer.VendorSdkOutsideAdapterRule.Id);
    }

    [Fact]
    public async Task VendorSdkOutsideAdapter_MatchesVendorNamespacesCaseInsensitively()
    {
        // Every other name convention in this assembly matches case-insensitively; the
        // vendor list was the one Ordinal comparison, which is the same
        // looks-covered-isn't failure in a smaller form.
        var diagnostics = await AnalyzerTestHelper.GetDiagnosticsAsync(
            new AntiCorruptionEdgeAnalyzer(),
            ("src/Demo.Api/Application/Pay.cs",
                "using STRIPE.Checkout;\n\nnamespace Demo.Api.Application;\n\npublic class Pay { }\n"));

        Assert.Single(diagnostics, d => d.Id == AntiCorruptionEdgeAnalyzer.VendorSdkOutsideAdapterRule.Id);
    }

    [Fact]
    public async Task VendorSdkOutsideAdapter_HonoursAConfiguredAdapterFolderName()
    {
        // A team whose adapters live in External/ used to get flagged on every vendor
        // import, with silencing the rule as the only recourse — the false-positive
        // direction of the convention gap, which is worse than the silent one.
        var config = new Dictionary<string, string>
        {
            [PortcullisConventionKeys.AdapterFolders] = "External",
        };

        var diagnostics = await AnalyzerTestHelper.GetDiagnosticsAsync(
            new AntiCorruptionEdgeAnalyzer(), config,
            ("src/Demo.Api/External/StripeGateway.cs",
                "using Stripe;\n\nnamespace Demo.Api.External;\n\npublic class StripeGateway { }\n"));

        Assert.DoesNotContain(diagnostics, d => d.Id == AntiCorruptionEdgeAnalyzer.VendorSdkOutsideAdapterRule.Id);
    }

    [Fact]
    public async Task VendorSdkOutsideAdapter_HonoursAConfiguredVendorNamespace()
    {
        var config = new Dictionary<string, string>
        {
            [PortcullisConventionKeys.VendorNamespaces] = "Acme.Payments",
        };

        var diagnostics = await AnalyzerTestHelper.GetDiagnosticsAsync(
            new AntiCorruptionEdgeAnalyzer(), config,
            ("src/Demo.Api/Application/Pay.cs",
                "using Acme.Payments.Client;\n\nnamespace Demo.Api.Application;\n\npublic class Pay { }\n"));

        Assert.Single(diagnostics, d => d.Id == AntiCorruptionEdgeAnalyzer.VendorSdkOutsideAdapterRule.Id);
    }
}
