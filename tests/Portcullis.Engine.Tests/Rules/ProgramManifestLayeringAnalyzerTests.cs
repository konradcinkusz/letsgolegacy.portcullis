using Portcullis.Rules;

namespace Portcullis.Engine.Tests.Rules;

public class ProgramManifestLayeringAnalyzerTests
{
    [Fact]
    public async Task ControllerNoDbContext_FiresWhenControllerFieldIsADbContext()
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

        var diagnostics = await AnalyzerTestHelper.GetDiagnosticsAsync(
            new ProgramManifestLayeringAnalyzer(),
            ("src/Demo/FooController.cs", source));

        var hit = Assert.Single(diagnostics, d => d.Id == ProgramManifestLayeringAnalyzer.ControllerNoDbContextRule.Id);
        Assert.Contains("FooController", hit.GetMessage());
        Assert.Contains("FooDbContext", hit.GetMessage());
    }

    [Fact]
    public async Task ControllerNoDbContext_DoesNotFireWhenControllerOnlyReferencesAnOrchestrator()
    {
        const string source = """
            namespace Demo;

            public class FooOrchestrator
            {
                public string Handle() => "ok";
            }

            [ApiController]
            public class FooController : ControllerBase
            {
                private readonly FooOrchestrator _orchestrator;

                public FooController(FooOrchestrator orchestrator)
                {
                    _orchestrator = orchestrator;
                }
            }
            """;

        var diagnostics = await AnalyzerTestHelper.GetDiagnosticsAsync(
            new ProgramManifestLayeringAnalyzer(),
            ("src/Demo/FooController.cs", source));

        Assert.DoesNotContain(diagnostics, d => d.Id == ProgramManifestLayeringAnalyzer.ControllerNoDbContextRule.Id);
    }

    [Fact]
    public async Task OrphanEntity_FiresWhenDbSetEntityHasNoReferenceOutsideItsOwnFile()
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

        var diagnostics = await AnalyzerTestHelper.GetDiagnosticsAsync(
            new ProgramManifestLayeringAnalyzer(),
            ("src/Demo/Domain/Widget.cs", domainSource),
            ("src/Demo/Infrastructure/DemoDbContext.cs", dbContextSource));

        var hit = Assert.Single(diagnostics, d => d.Id == ProgramManifestLayeringAnalyzer.OrphanEntityRule.Id);
        Assert.Contains("Widget", hit.GetMessage());
    }

    [Fact]
    public async Task OrphanEntity_DoesNotFireWhenEntityIsReferencedElsewhere()
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
        const string controllerSource = """
            namespace Demo.Controllers;

            using Demo.Domain;

            [ApiController]
            public class WidgetController : ControllerBase
            {
                public Widget Get() => new Widget();
            }
            """;

        var diagnostics = await AnalyzerTestHelper.GetDiagnosticsAsync(
            new ProgramManifestLayeringAnalyzer(),
            ("src/Demo/Domain/Widget.cs", domainSource),
            ("src/Demo/Infrastructure/DemoDbContext.cs", dbContextSource),
            ("src/Demo/Controllers/WidgetController.cs", controllerSource));

        Assert.DoesNotContain(diagnostics, d => d.Id == ProgramManifestLayeringAnalyzer.OrphanEntityRule.Id);
    }
}
