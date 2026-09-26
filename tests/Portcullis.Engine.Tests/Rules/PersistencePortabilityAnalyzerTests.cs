using Portcullis.Rules;

namespace Portcullis.Engine.Tests.Rules;

public class PersistencePortabilityAnalyzerTests
{
    [Fact]
    public async Task EnsureCreated_FiresWhenCalledWithNoInMemoryGuard()
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

        var diagnostics = await AnalyzerTestHelper.GetDiagnosticsAsync(
            new PersistencePortabilityAnalyzer(),
            ("src/Demo/Infrastructure/DatabaseInitializer.cs", source));

        var hit = Assert.Single(diagnostics, d => d.Id == PersistencePortabilityAnalyzer.EnsureCreatedOutsideTestRule.Id);
        Assert.Contains("EnsureCreatedAsync", hit.GetMessage());
    }

    [Fact]
    public async Task EnsureCreated_DoesNotFireWhenGuardedByIsInMemoryCheck()
    {
        // The real, current shape of the reference consumer app's Consumer.ServiceDefaults/DatabaseExtensions.cs.
        const string source = """
            namespace Demo.Infrastructure;

            public static class DatabaseInitializer
            {
                public static async Task InitializeAsync(FooDbContext context, CancellationToken cancellationToken)
                {
                    if (context.Database.IsInMemory())
                    {
                        await context.Database.EnsureCreatedAsync(cancellationToken);
                        return;
                    }

                    await context.Database.MigrateAsync(cancellationToken);
                }
            }

            public class FooDbContext : DbContext
            {
            }
            """;

        var diagnostics = await AnalyzerTestHelper.GetDiagnosticsAsync(
            new PersistencePortabilityAnalyzer(),
            ("src/Demo/Infrastructure/DatabaseInitializer.cs", source));

        Assert.DoesNotContain(diagnostics, d => d.Id == PersistencePortabilityAnalyzer.EnsureCreatedOutsideTestRule.Id);
    }

    [Fact]
    public async Task EnsureCreated_DoesNotFireWhenNoSuchCallExists()
    {
        const string source = """
            namespace Demo.Infrastructure;

            public static class DatabaseInitializer
            {
                public static async Task InitializeAsync(FooDbContext context, CancellationToken cancellationToken)
                {
                    await context.Database.MigrateAsync(cancellationToken);
                }
            }

            public class FooDbContext : DbContext
            {
            }
            """;

        var diagnostics = await AnalyzerTestHelper.GetDiagnosticsAsync(
            new PersistencePortabilityAnalyzer(),
            ("src/Demo/Infrastructure/DatabaseInitializer.cs", source));

        Assert.DoesNotContain(diagnostics, d => d.Id == PersistencePortabilityAnalyzer.EnsureCreatedOutsideTestRule.Id);
    }

    [Fact]
    public async Task SeedData_FiresWhenHasDataIsCalledInsideOnModelCreating()
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

        var diagnostics = await AnalyzerTestHelper.GetDiagnosticsAsync(
            new PersistencePortabilityAnalyzer(),
            ("src/Demo/Infrastructure/FooDbContext.cs", source));

        var hit = Assert.Single(diagnostics, d => d.Id == PersistencePortabilityAnalyzer.SeedDataInModelRule.Id);
        Assert.Contains("FooDbContext", hit.GetMessage());
    }

    [Fact]
    public async Task SeedData_DoesNotFireWhenHasDataIsCalledOutsideOnModelCreating()
    {
        const string source = """
            namespace Demo.Infrastructure;

            public class FooDbContext : DbContext
            {
                public void SeedManually(ModelBuilder modelBuilder)
                {
                    modelBuilder.Entity<Widget>().HasData(new Widget { Id = 1, Name = "Default" });
                }
            }
            """;

        var diagnostics = await AnalyzerTestHelper.GetDiagnosticsAsync(
            new PersistencePortabilityAnalyzer(),
            ("src/Demo/Infrastructure/FooDbContext.cs", source));

        Assert.DoesNotContain(diagnostics, d => d.Id == PersistencePortabilityAnalyzer.SeedDataInModelRule.Id);
    }

    [Fact]
    public async Task SeedData_DoesNotFireWhenOnModelCreatingHasNoHasDataCall()
    {
        const string source = """
            namespace Demo.Infrastructure;

            public class FooDbContext : DbContext
            {
                protected override void OnModelCreating(ModelBuilder modelBuilder)
                {
                    modelBuilder.Entity<Widget>().ToTable("widgets");
                }
            }
            """;

        var diagnostics = await AnalyzerTestHelper.GetDiagnosticsAsync(
            new PersistencePortabilityAnalyzer(),
            ("src/Demo/Infrastructure/FooDbContext.cs", source));

        Assert.DoesNotContain(diagnostics, d => d.Id == PersistencePortabilityAnalyzer.SeedDataInModelRule.Id);
    }
}
