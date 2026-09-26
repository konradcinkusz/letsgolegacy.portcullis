using Microsoft.CodeAnalysis;
using Portcullis.Rules;

namespace Portcullis.Engine.Tests.Rules;

/// <summary>
/// PORTCULLIS_MIG_HTTPCONTEXT_CURRENT: every way of reaching the static
/// <c>System.Web.HttpContext.Current</c> is one report, and nothing else named
/// <c>Current</c> — a team's own ambient context, an instance member, a <c>nameof</c> — is.
/// </summary>
public class HttpContextCurrentAnalyzerTests
{
    private static readonly string RuleId = HttpContextCurrentAnalyzer.HttpContextCurrentRule.Id;

    private static async Task<List<Diagnostic>> RunAsync(string source, string path = "src/Shop/Ambient.cs")
    {
        var diagnostics = await AnalyzerTestHelper.GetDiagnosticsAsync(new HttpContextCurrentAnalyzer(), (path, source));
        // Concurrent execution reports in no fixed order; tests read them in source order.
        return diagnostics.Where(d => d.Id == RuleId).OrderBy(d => d.Location.SourceSpan.Start).ToList();
    }

    private static string Located(Diagnostic diagnostic) =>
        diagnostic.Location.SourceTree!.GetText().ToString(diagnostic.Location.SourceSpan);

    [Fact]
    public async Task FiresOnHttpContextCurrentReachedThroughUsingSystemWeb()
    {
        var hits = await RunAsync("""
            using System.Web;

            namespace Shop;

            public static class Ambient
            {
                public static object User() => HttpContext.Current.User;
            }
            """);

        var hit = Assert.Single(hits);
        Assert.Equal("HttpContext.Current", Located(hit));
        Assert.Contains("'HttpContext.Current'", hit.GetMessage());
        Assert.Equal(DiagnosticSeverity.Error, hit.Severity);
    }

    [Theory]
    [InlineData("fully qualified", "", "System.Web.HttpContext.Current", "System.Web.HttpContext.Current")]
    [InlineData("alias", "using Ctx = System.Web.HttpContext;", "Ctx.Current", "Ctx.Current")]
    [InlineData("using static", "using static System.Web.HttpContext;", "Current", "Current")]
    public async Task FiresOnEveryWayOfNamingIt(string shape, string import, string expression, string expectedLocation)
    {
        var hits = await RunAsync($$"""
            {{import}}

            namespace Shop;

            public static class Ambient
            {
                public static object Read() => {{expression}};
            }
            """);

        Assert.True(hits.Count == 1, $"{shape}: expected one report, got {hits.Count}");
        Assert.Equal(expectedLocation, Located(hits[0]));
    }

    [Fact]
    public async Task FiresOnAWriteToo()
    {
        // Tests and legacy bootstrap code assign it; that is the same static dependency.
        var hits = await RunAsync("""
            using System.Web;

            namespace Shop;

            public static class Fixture
            {
                public static void Reset() => HttpContext.Current = null;
            }
            """);

        Assert.Equal("HttpContext.Current", Located(Assert.Single(hits)));
    }

    [Fact]
    public async Task FiresEvenWhenTheEnclosingClassHasAnUnresolvedBase()
    {
        // `.Current` exists only on the System.Web type, so unlike a bare `HttpContext` this
        // expression cannot be an inherited member in disguise.
        var hits = await RunAsync("""
            using System.Web;

            namespace Shop;

            public class LegacyModule : SomeFrameworkBase
            {
                public object Read() => HttpContext.Current.Items;
            }
            """);

        Assert.Single(hits);
    }

    [Fact]
    public async Task DoesNotFireOnATeamsOwnStaticCurrent()
    {
        var hits = await RunAsync("""
            namespace Shop
            {
                public sealed class HttpContext
                {
                    public static HttpContext Current => null;
                }

                public static class Reader
                {
                    public static object Read() => HttpContext.Current;
                }
            }
            """);

        Assert.Empty(hits);
    }

    [Fact]
    public async Task DoesNotFireOnInstanceMembersOrTheAbstractions()
    {
        var hits = await RunAsync("""
            using System.Web;

            namespace Shop;

            public static class Reader
            {
                public static object Items(HttpContext context) => context.Items;
                public static object Wrapped(HttpContextBase context) => context.Items;
            }
            """);

        Assert.Empty(hits);
    }

    [Fact]
    public async Task DoesNotFireInsideNameof()
    {
        var hits = await RunAsync("""
            namespace Shop;

            public static class Names
            {
                public const string Property = nameof(System.Web.HttpContext.Current);
            }
            """);

        Assert.Empty(hits);
    }

    [Fact]
    public async Task DoesNotFireInAConfiguredMigrationExemptFolder()
    {
        const string source = """
            using System.Web;

            namespace Shop;

            public static class Ambient
            {
                public static object User() => HttpContext.Current.User;
            }
            """;
        var configuration = new Dictionary<string, string>
        {
            [PortcullisConventionKeys.MigrationExemptFolders] = "Compat",
        };

        var diagnostics = await AnalyzerTestHelper.GetDiagnosticsAsync(
            new HttpContextCurrentAnalyzer(), configuration,
            ("src/Compat/Ambient.cs", source), ("src/Shop/Ambient.cs", source));

        var hit = Assert.Single(diagnostics, d => d.Id == RuleId);
        Assert.Equal("src/Shop/Ambient.cs", hit.Location.SourceTree!.FilePath);
    }

    [Fact]
    public async Task ReportsTheFullMessage()
    {
        var hits = await RunAsync("""
            using System.Web;

            namespace Shop;

            public static class Ambient
            {
                public static object User() => HttpContext.Current.User;
            }
            """);

        Assert.Equal(
            "'HttpContext.Current' reaches the current request through the static System.Web.HttpContext.Current, " +
            "which works only on a request's own flow and exists on ASP.NET Core only through the " +
            "System.Web adapters. Inject IHttpContextAccessor, or pass the values in, instead.",
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
                public static object User() => System.Web.HttpContext.Current.User;
            }
            """);

        Assert.Empty(hits);
    }

    [Fact]
    public async Task DoesNotFireOnAnInstanceMemberThatHappensToBeCalledCurrent()
    {
        // Only the static property is the ambient one. A shim declaring an instance
        // `Current` (here, alone in its own System.Web) is not reported.
        const string systemWeb = """
            namespace System.Web
            {
                public sealed class HttpContext
                {
                    public HttpContext Current => this;
                }
            }
            """;
        const string consumer = """
            namespace Shop;

            public static class Chain
            {
                public static object Read(System.Web.HttpContext context) => context.Current;
            }
            """;

        var diagnostics = await AnalyzerTestHelper.GetDiagnosticsAgainstCoreLibraryOnlyAsync(
            new HttpContextCurrentAnalyzer(), ("src/Compat/SystemWeb.cs", systemWeb), ("src/Shop/Chain.cs", consumer));

        Assert.DoesNotContain(diagnostics, d => d.Id == RuleId);
    }

    [Fact]
    public async Task RecognisesHttpContextDeclaredByAnyAssembly()
    {
        const string systemWeb = """
            namespace System.Web
            {
                public sealed class HttpContext
                {
                    public static HttpContext Current { get; set; }
                }
            }
            """;
        const string consumer = """
            namespace Shop;

            public static class Ambient
            {
                public static object Read() => System.Web.HttpContext.Current;
            }
            """;

        var diagnostics = await AnalyzerTestHelper.GetDiagnosticsAgainstCoreLibraryOnlyAsync(
            new HttpContextCurrentAnalyzer(), ("src/Compat/SystemWeb.cs", systemWeb), ("src/Shop/Ambient.cs", consumer));

        Assert.Single(diagnostics, d => d.Id == RuleId);
    }
}
