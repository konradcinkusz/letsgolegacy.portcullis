using Portcullis.Rules;
using Portcullis.Engine.Tests.Rules.Mutants;

namespace Portcullis.Engine.Tests.Rules;

/// <summary>
/// The mutation pass: for each of the 5 diagnostics across Track A's 3 rules, a
/// deliberately broken variant lives under Rules/Mutants/, swapped in place of the real
/// analyzer for exactly this test — same fixture, same assertion shape, only the
/// analyzer instance differs. Each test asserts, in order: the real rule catches the
/// target violation (the sanity check — without it, "the mutant missed it" proves
/// nothing), then the mutant does not. Results are recorded honestly in
/// docs/MUTATIONS.md, including the one variant that survived on first attempt and the
/// fix that followed (see that file's "Survivors" section).
/// </summary>
public class MutationTests
{
    [Fact]
    public async Task KernelLocCeiling_RealCatchesButMutantMisses()
    {
        var files = new (string Path, string Source)[]
        {
            ("src/Demo.ServiceDefaults/FileA.cs", "namespace Demo.ServiceDefaults;\n" + Padding(500)),
            ("src/Demo.ServiceDefaults/FileB.cs", "namespace Demo.ServiceDefaults;\n" + Padding(400)),
        };

        var real = await AnalyzerTestHelper.GetDiagnosticsAsync(new KernelBoundaryAnalyzer(), files);
        Assert.Contains(real, d => d.Id == KernelBoundaryAnalyzer.LocCeilingRule.Id);

        var mutant = await AnalyzerTestHelper.GetDiagnosticsAsync(new KernelLocCeilingMutant(), files);
        Assert.DoesNotContain(mutant, d => d.Id == KernelBoundaryAnalyzer.LocCeilingRule.Id);
    }

    [Fact]
    public async Task KernelEntityReference_RealCatchesButMutantMisses()
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
        var files = new[]
        {
            ("src/Demo.ServiceDefaults/Extensions.cs", kernelSource),
            ("src/Demo.AdvertsService/Domain/Advert.cs", domainSource),
        };

        var real = await AnalyzerTestHelper.GetDiagnosticsAsync(new KernelBoundaryAnalyzer(), files);
        Assert.Contains(real, d => d.Id == KernelBoundaryAnalyzer.KernelEntityReferenceRule.Id);

        var mutant = await AnalyzerTestHelper.GetDiagnosticsAsync(new KernelEntityReferenceMutant(), files);
        Assert.DoesNotContain(mutant, d => d.Id == KernelBoundaryAnalyzer.KernelEntityReferenceRule.Id);
    }

    [Fact]
    public async Task ControllerNoDbContext_RealCatchesButMutantMisses()
    {
        const string source = """
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
        var files = new[] { ("src/Demo/FooController.cs", source) };

        var real = await AnalyzerTestHelper.GetDiagnosticsAsync(new ProgramManifestLayeringAnalyzer(), files);
        Assert.Contains(real, d => d.Id == ProgramManifestLayeringAnalyzer.ControllerNoDbContextRule.Id);

        var mutant = await AnalyzerTestHelper.GetDiagnosticsAsync(new ControllerNoDbContextMutant(), files);
        Assert.DoesNotContain(mutant, d => d.Id == ProgramManifestLayeringAnalyzer.ControllerNoDbContextRule.Id);
    }

    [Fact]
    public async Task OrphanEntity_RealCatchesButMutantMisses()
    {
        const string domainSource = """
            namespace Demo.Domain;

            public class Widget
            {
            }
            """;
        const string dbContextSource = """
            namespace Demo.Infrastructure;

            using Demo.Domain;

            public class DemoDbContext : DbContext
            {
                public DbSet<Widget> Widgets => Set<Widget>();
            }
            """;
        var files = new[]
        {
            ("src/Demo/Domain/Widget.cs", domainSource),
            ("src/Demo/Infrastructure/DemoDbContext.cs", dbContextSource),
        };

        var real = await AnalyzerTestHelper.GetDiagnosticsAsync(new ProgramManifestLayeringAnalyzer(), files);
        Assert.Contains(real, d => d.Id == ProgramManifestLayeringAnalyzer.OrphanEntityRule.Id);

        var mutant = await AnalyzerTestHelper.GetDiagnosticsAsync(new OrphanEntityMutant(), files);
        Assert.DoesNotContain(mutant, d => d.Id == ProgramManifestLayeringAnalyzer.OrphanEntityRule.Id);
    }

    [Fact]
    public async Task CustomBaseClass_RealCatchesButMutantMisses()
    {
        const string source = """
            namespace Demo;

            public abstract class ModuleBase
            {
            }

            public class PaymentModule : ModuleBase
            {
            }
            """;
        var files = new[] { ("src/Demo/PaymentModule.cs", source) };

        var real = await AnalyzerTestHelper.GetDiagnosticsAsync(new ExtensibilityInheritanceAnalyzer(), files);
        Assert.Contains(real, d => d.Id == ExtensibilityInheritanceAnalyzer.CustomBaseClassRule.Id);

        var mutant = await AnalyzerTestHelper.GetDiagnosticsAsync(new CustomBaseClassMutant(), files);
        Assert.DoesNotContain(mutant, d => d.Id == ExtensibilityInheritanceAnalyzer.CustomBaseClassRule.Id);
    }

    [Fact]
    public async Task EnsureCreatedOutsideTest_RealCatchesButMutantMisses()
    {
        const string source = """
            namespace Demo.Infrastructure;

            public static class DatabaseInitializer
            {
                public static async Task InitializeAsync(FooDbContext context, CancellationToken cancellationToken)
                {
                    await context.Database.EnsureCreatedAsync(cancellationToken);
                }
            }

            public class FooDbContext : DbContext
            {
            }
            """;
        var files = new[] { ("src/Demo/Infrastructure/DatabaseInitializer.cs", source) };

        var real = await AnalyzerTestHelper.GetDiagnosticsAsync(new PersistencePortabilityAnalyzer(), files);
        Assert.Contains(real, d => d.Id == PersistencePortabilityAnalyzer.EnsureCreatedOutsideTestRule.Id);

        var mutant = await AnalyzerTestHelper.GetDiagnosticsAsync(new EnsureCreatedAsyncVariantDroppedMutant(), files);
        Assert.DoesNotContain(mutant, d => d.Id == PersistencePortabilityAnalyzer.EnsureCreatedOutsideTestRule.Id);
    }

    [Fact]
    public async Task SeedDataInModel_RealCatchesButMutantMisses()
    {
        const string source = """
            namespace Demo.Infrastructure;

            public class FooDbContext : DbContext
            {
                protected override void OnModelCreating(ModelBuilder modelBuilder)
                {
                    modelBuilder.Entity<Widget>().HasData(new Widget { Id = 1, Name = "Default" });
                }
            }
            """;
        var files = new[] { ("src/Demo/Infrastructure/FooDbContext.cs", source) };

        var real = await AnalyzerTestHelper.GetDiagnosticsAsync(new PersistencePortabilityAnalyzer(), files);
        Assert.Contains(real, d => d.Id == PersistencePortabilityAnalyzer.SeedDataInModelRule.Id);

        var mutant = await AnalyzerTestHelper.GetDiagnosticsAsync(new OnModelCreatingTypoMutant(), files);
        Assert.DoesNotContain(mutant, d => d.Id == PersistencePortabilityAnalyzer.SeedDataInModelRule.Id);
    }

    [Fact]
    public async Task VendorSdkOutsideAdapter_RealCatchesButMutantMisses()
    {
        const string source = """
            using Anthropic.Models.Messages;

            namespace Demo.AgentService.Application;

            public class AgentOrchestrator
            {
                public MessageParam? Build() => null;
            }
            """;
        var files = new[] { ("src/Demo.AgentService/Application/AgentOrchestrator.cs", source) };

        var real = await AnalyzerTestHelper.GetDiagnosticsAsync(new AntiCorruptionEdgeAnalyzer(), files);
        Assert.Contains(real, d => d.Id == AntiCorruptionEdgeAnalyzer.VendorSdkOutsideAdapterRule.Id);

        var mutant = await AnalyzerTestHelper.GetDiagnosticsAsync(new AdapterSegmentOverbroadMutant(), files);
        Assert.DoesNotContain(mutant, d => d.Id == AntiCorruptionEdgeAnalyzer.VendorSdkOutsideAdapterRule.Id);
    }

    [Fact]
    public async Task MissingServiceDefaults_RealCatchesButMutantMisses()
    {
        const string source = """
            var builder = Host.CreateApplicationBuilder(args);

            var host = builder.Build();
            await host.RunAsync();
            """;
        var files = new[] { ("src/Demo.Jobs.Expiry/Program.cs", source) };

        var real = await AnalyzerTestHelper.GetDiagnosticsAsync(new ObservabilityBuildTimeAnalyzer(), files);
        Assert.Contains(real, d => d.Id == ObservabilityBuildTimeAnalyzer.MissingServiceDefaultsRule.Id);

        var mutant = await AnalyzerTestHelper.GetDiagnosticsAsync(new HostBuilderFactoryNarrowedMutant(), files);
        Assert.DoesNotContain(mutant, d => d.Id == ObservabilityBuildTimeAnalyzer.MissingServiceDefaultsRule.Id);
    }

    private static string Padding(int lines) =>
        string.Join('\n', Enumerable.Range(0, lines).Select(i => $"// padding line {i}"));

    [Fact]
    public async Task ConventionCoverageAggregate_RealStaysSilentButMutantFires()
    {
        // Inverted relative to every other mutation test here, deliberately. For the
        // principle rules the failure that matters is a missed violation, so those tests
        // assert "real catches, mutant misses". For a meta-rule the failure that matters
        // is the opposite: one that fires on healthy code gets switched off, and then
        // reports nothing when it counts. So this asserts "real is silent, mutant is not".
        var files = new (string Path, string Source)[]
        {
            ("src/Demo.ServiceDefaults/Extensions.cs", "namespace Demo.ServiceDefaults;\n\npublic class E { }\n"),
            ("src/Demo.Api/Thing.cs", "namespace Demo.Api;\n\npublic class Thing { }\n"),
        };

        // A kernel folder matched, so the conventions are demonstrably reaching this
        // codebase and the aggregate rule has nothing to report.
        var real = await AnalyzerTestHelper.GetDiagnosticsAsync(new ConventionCoverageAnalyzer(), files);
        Assert.DoesNotContain(real, d => d.Id == ConventionCoverageAnalyzer.NoConventionMatchedRule.Id);

        var mutant = await AnalyzerTestHelper.GetDiagnosticsAsync(new ConventionCoverageAggregateMutant(), files);
        Assert.Contains(mutant, d => d.Id == ConventionCoverageAnalyzer.NoConventionMatchedRule.Id);
    }
}
