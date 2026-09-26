using Microsoft.CodeAnalysis;
using Portcullis.Rules;

namespace Portcullis.Engine.Tests.Rules;

/// <summary>
/// PORTCULLIS_MIG_SYSTEM_WEB. The positive cases cover each way code reaches System.Web —
/// a using directive, a qualified name, a type reference, an attribute, an alias — and the
/// negative ones the look-alikes a text search would flag and a symbol check must not:
/// ASP.NET Core's own <c>HttpContext</c>, a team's <c>HttpContext</c>, and the two System.Web
/// types modern .NET still ships.
/// </summary>
public class SystemWebUsageAnalyzerTests
{
    private static readonly string RuleId = SystemWebUsageAnalyzer.SystemWebRule.Id;

    private static async Task<List<Diagnostic>> RunAsync(string source, string path = "src/Shop/Legacy.cs")
    {
        var diagnostics = await AnalyzerTestHelper.GetDiagnosticsAsync(new SystemWebUsageAnalyzer(), (path, source));
        // Concurrent execution reports in no fixed order; tests read them in source order.
        return diagnostics.Where(d => d.Id == RuleId).OrderBy(d => d.Location.SourceSpan.Start).ToList();
    }

    private static string Located(Diagnostic diagnostic) =>
        diagnostic.Location.SourceTree!.GetText().ToString(diagnostic.Location.SourceSpan);

    // --- using directives ---

    [Fact]
    public async Task FiresOnAUsingDirectiveForALegacySystemWebNamespace()
    {
        // System.Web.Mvc is not declared in a scan; the import still anchors on System.Web.
        var hits = await RunAsync("""
            using System.Web.Mvc;

            namespace Shop;

            public class Catalogue { }
            """);

        var hit = Assert.Single(hits);
        Assert.Equal("using System.Web.Mvc;", Located(hit));
        Assert.Contains("'System.Web.Mvc'", hit.GetMessage());
    }

    [Fact]
    public async Task FiresOnAUsingDirectiveThatBindsToALegacyNamespace()
    {
        var hits = await RunAsync("""
            using System.Web.SessionState;

            namespace Shop;

            public class Basket { }
            """);

        Assert.Equal("using System.Web.SessionState;", Located(Assert.Single(hits)));
    }

    [Fact]
    public async Task FiresOnUsingStaticAndOnAnAliasOfALegacyType()
    {
        var hits = await RunAsync("""
            using static System.Web.HttpRuntime;
            using Ctx = System.Web.HttpContext;

            namespace Shop;

            public class Paths { }
            """);

        Assert.Equal(
            new[] { "using static System.Web.HttpRuntime;", "using Ctx = System.Web.HttpContext;" },
            hits.Select(Located).ToArray());
    }

    [Fact]
    public async Task DoesNotFireOnUsingSystemWebWhenOnlyTheModernHttpUtilityIsUsed()
    {
        // System.Web.HttpUtility ships in .NET itself (System.Web.HttpUtility.dll).
        var hits = await RunAsync("""
            using System.Web;

            namespace Shop;

            public static class Links
            {
                public static string Encode(string value) => HttpUtility.UrlEncode(value);
            }
            """);

        Assert.Empty(hits);
    }

    // --- names ---

    [Fact]
    public async Task FiresOnATypeReferenceReachedThroughUsingSystemWeb()
    {
        var hits = await RunAsync("""
            using System.Web;

            namespace Shop;

            public class Audit
            {
                public void Record(HttpRequest request) { }
            }
            """);

        var hit = Assert.Single(hits);
        Assert.Equal("HttpRequest", Located(hit));
        Assert.Contains("'System.Web.HttpRequest'", hit.GetMessage());
    }

    [Fact]
    public async Task FiresOnceForAFullyQualifiedReference_AtTheQualifiedType()
    {
        // Three names bind here (Web, HttpContext, Current); one reference is one report.
        var hits = await RunAsync("""
            namespace Shop;

            public static class Session
            {
                public static object Current() => System.Web.HttpContext.Current;
            }
            """);

        Assert.Equal("System.Web.HttpContext", Located(Assert.Single(hits)));
    }

    [Fact]
    public async Task FiresOnObjectCreationTypeofCatchAndGenericArguments()
    {
        var hits = await RunAsync("""
            using System;
            using System.Collections.Generic;

            namespace Shop;

            public static class Cookies
            {
                public static object Make() => new System.Web.HttpCookie("basket");
                public static Type Runtime() => typeof(System.Web.HttpRuntime);
                public static List<System.Web.HttpCookie> None() => new();

                public static void Guard(Action action)
                {
                    try { action(); }
                    catch (System.Web.HttpException) { }
                }
            }
            """);

        Assert.Equal(
            new[] { "System.Web.HttpCookie", "System.Web.HttpRuntime", "System.Web.HttpCookie", "System.Web.HttpException" },
            hits.OrderBy(h => h.Location.SourceSpan.Start).Select(Located).ToArray());
    }

    [Fact]
    public async Task FiresOnAnAttributeFromALegacyNamespace()
    {
        // An attribute name binds to the attribute's constructor, not its type.
        const string mvc = """
            namespace System.Web.Mvc
            {
                public sealed class AuthorizeAttribute : System.Attribute { }
            }
            """;
        const string controller = """
            namespace Shop;

            [System.Web.Mvc.Authorize]
            public class OrdersEndpoint { }
            """;

        var diagnostics = await AnalyzerTestHelper.GetDiagnosticsAsync(
            new SystemWebUsageAnalyzer(), ("src/Compat/Mvc.cs", mvc), ("src/Shop/OrdersEndpoint.cs", controller));

        var hit = Assert.Single(diagnostics, d => d.Id == RuleId);
        Assert.Equal("System.Web.Mvc.Authorize", Located(hit));
        Assert.Contains("'System.Web.Mvc.AuthorizeAttribute'", hit.GetMessage());
    }

    [Fact]
    public async Task FiresAtTheBoundSystemWebPrefixOfAnUnresolvedLegacyName()
    {
        var hits = await RunAsync("""
            namespace Shop;

            public class HomeController : System.Web.Mvc.Controller { }
            """);

        var hit = Assert.Single(hits);
        Assert.Equal("System.Web", Located(hit));
        Assert.Contains("'System.Web'", hit.GetMessage());
    }

    [Fact]
    public async Task FiresOnAnAliasUsedAsAType()
    {
        var hits = await RunAsync("""
            using Request = System.Web.HttpRequest;

            namespace Shop;

            public class Audit
            {
                public void Record(Request request) { }
            }
            """);

        // The directive, and the parameter type written through the alias.
        Assert.Equal(new[] { "using Request = System.Web.HttpRequest;", "Request" }, hits.Select(Located).ToArray());
    }

    [Fact]
    public async Task FiresOnAnUnqualifiedNameWhenTheEnclosingTypeHasNoUnresolvedBase()
    {
        var hits = await RunAsync("""
            using System.Web;

            namespace Shop;

            public static class Ambient
            {
                public static object User() => HttpContext.Current.User;
            }
            """);

        Assert.Equal("HttpContext", Located(Assert.Single(hits)));
    }

    [Fact]
    public async Task StillFiresInATypePositionInsideAClassWithAnUnresolvedBase()
    {
        // A field's type cannot be captured by an inherited member, so the guard below does
        // not apply to it.
        var hits = await RunAsync("""
            using System.Web;

            namespace Shop;

            public class OrdersController : ControllerBase
            {
                private HttpCookie _last;
            }
            """);

        Assert.Equal("HttpCookie", Located(Assert.Single(hits)));
    }

    [Fact]
    public async Task DoesNotFireOnAnUnqualifiedExpressionNameInsideAClassWithAnUnresolvedBase()
    {
        // The migrated ASP.NET Core controller: `HttpContext` here is ControllerBase's own
        // property, which the scan cannot see because ASP.NET Core is not referenced.
        var hits = await RunAsync("""
            using System.Web;

            namespace Shop;

            public class OrdersController : ControllerBase
            {
                public string Encoded(string value) => HttpUtility.UrlEncode(value) + HttpContext.Request;
            }
            """);

        Assert.Empty(hits);
    }

    [Fact]
    public async Task FiresOnANestedSystemWebType_NamingItByItsMetadataName()
    {
        const string systemWeb = """
            namespace System.Web
            {
                public class HttpCachePolicyShim
                {
                    public enum Scope { Private, Public }
                }
            }
            """;
        const string consumer = """
            namespace Shop;

            public static class Caching
            {
                public static object Scope() => System.Web.HttpCachePolicyShim.Scope.Public;
            }
            """;

        var diagnostics = await AnalyzerTestHelper.GetDiagnosticsAsync(
            new SystemWebUsageAnalyzer(), ("src/Compat/Cache.cs", systemWeb), ("src/Shop/Caching.cs", consumer));

        var hit = Assert.Single(diagnostics, d => d.Id == RuleId);
        Assert.Equal("System.Web.HttpCachePolicyShim.Scope", Located(hit));
        Assert.Contains("'System.Web.HttpCachePolicyShim+Scope'", hit.GetMessage());
    }

    [Fact]
    public async Task ReportsTheFullMessage()
    {
        var hits = await RunAsync("""
            using System.Web;

            namespace Shop;

            public class Audit
            {
                public void Record(HttpRequest request) { }
            }
            """);

        Assert.Equal(
            "'System.Web.HttpRequest' is System.Web, which modern .NET does not have: this builds only " +
            "through a compatibility shim or a leftover .NET Framework reference. Use the ASP.NET Core " +
            "equivalent (Microsoft.AspNetCore.*) instead.",
            Assert.Single(hits).GetMessage());
    }

    // --- look-alikes that must stay silent ---

    [Fact]
    public async Task DoesNotFireOnANamespaceThatOnlyStartsWithTheSameCharacters()
    {
        var hits = await RunAsync("""
            namespace System.WebHooks
            {
                public sealed class Receiver { }
            }

            namespace Shop
            {
                public class Hooks
                {
                    public System.WebHooks.Receiver Receiver { get; } = new System.WebHooks.Receiver();
                }
            }
            """);

        Assert.Empty(hits);
    }

    [Fact]
    public async Task DoesNotFireOnUsingStaticOrAliasesOfOtherTypesOrOfAnAllowedOne()
    {
        var hits = await RunAsync("""
            using static System.Math;
            using Clock = System.DateTime;
            using Encoder = System.Web.HttpUtility;

            namespace Shop;

            public class Plain { }
            """);

        Assert.Empty(hits);
    }

    [Fact]
    public async Task DoesNotFireOnAUsingDirectiveThatBindsToNothing()
    {
        var hits = await RunAsync("""
            using Contoso.Payments.Legacy;

            namespace Shop;

            public class Payments { }
            """);

        Assert.Empty(hits);
    }

    [Fact]
    public async Task DoesNotFireInGeneratedCode()
    {
        var hits = await RunAsync("""
            // <auto-generated />
            using System.Web.Mvc;

            namespace Shop;

            public class Designer
            {
                public System.Web.HttpCookie Cookie;
            }
            """);

        Assert.Empty(hits);
    }

    [Fact]
    public async Task DoesNotFireOnAspNetCoresHttpContext()
    {
        const string aspNetCore = """
            namespace Microsoft.AspNetCore.Http
            {
                public abstract class HttpContext
                {
                    public abstract object Items { get; }
                }
            }
            """;
        const string endpoint = """
            using Microsoft.AspNetCore.Http;

            namespace Shop;

            public static class Endpoint
            {
                public static object Items(HttpContext context) => context.Items;
            }
            """;

        var diagnostics = await AnalyzerTestHelper.GetDiagnosticsAsync(
            new SystemWebUsageAnalyzer(), ("src/Stubs/Http.cs", aspNetCore), ("src/Shop/Endpoint.cs", endpoint));

        Assert.DoesNotContain(diagnostics, d => d.Id == RuleId);
    }

    [Fact]
    public async Task DoesNotFireOnATeamsOwnHttpContextInAnotherNamespace()
    {
        var hits = await RunAsync("""
            namespace Shop.Web
            {
                public class HttpContext
                {
                    public static HttpContext Current => null;
                }

                public static class Reader
                {
                    public static object Read() => Shop.Web.HttpContext.Current;
                }
            }
            """);

        Assert.Empty(hits);
    }

    [Fact]
    public async Task DoesNotFireOnTheModernHttpUtilityWrittenFullyQualified()
    {
        var hits = await RunAsync("""
            namespace Shop;

            public static class Links
            {
                public static string Encode(string value) => System.Web.HttpUtility.HtmlEncode(value);
            }
            """);

        Assert.Empty(hits);
    }

    [Fact]
    public async Task DoesNotFireOnTheModernIHtmlString()
    {
        var hits = await RunAsync("""
            namespace Shop;

            public static class Markup
            {
                public static string Render(System.Web.IHtmlString html) => html.ToHtmlString();
            }
            """);

        Assert.Empty(hits);
    }

    [Fact]
    public void Defaults_AllowExactlyTheTwoSystemWebTypesModernDotNetShips_AndExemptNothing()
    {
        // System.Web.HttpUtility.dll in the .NET runtime declares these two and nothing else
        // in System.Web; everything else in the namespace exists only in the .NET Framework.
        Assert.Equal(
            new[] { "System.Web.HttpUtility", "System.Web.IHtmlString" },
            PortcullisConventions.Default.SystemWebAllowedTypes.ToArray());
        Assert.Empty(PortcullisConventions.Default.MigrationExemptFolders);
    }

    [Fact]
    public async Task DoesNotFireInDocumentationCommentsOrNamespaceDeclarations()
    {
        var hits = await RunAsync("""
            namespace System.Web.Compatibility
            {
                /// <summary>Replaces <see cref="System.Web.HttpContext"/> for the ported code.</summary>
                public class RequestFacade { }
            }
            """);

        Assert.Empty(hits);
    }

    // --- configuration ---

    [Fact]
    public async Task DoesNotFireInAConfiguredMigrationExemptFolder()
    {
        const string source = """
            using System.Web;

            namespace Shop;

            public class Audit
            {
                public void Record(HttpRequest request) { }
            }
            """;
        var configuration = new Dictionary<string, string>
        {
            [PortcullisConventionKeys.MigrationExemptFolders] = "Legacy",
        };

        var diagnostics = await AnalyzerTestHelper.GetDiagnosticsAsync(
            new SystemWebUsageAnalyzer(), configuration,
            ("src/Legacy/Audit.cs", source), ("src/Shop/Audit.cs", source));

        var hit = Assert.Single(diagnostics, d => d.Id == RuleId);
        Assert.Equal("src/Shop/Audit.cs", hit.Location.SourceTree!.FilePath);
    }

    [Fact]
    public async Task DoesNotFireOnATypeTheTeamAllowed()
    {
        const string source = """
            using System.Web;

            namespace Shop;

            public class Audit
            {
                public void Record(HttpRequest request, HttpCookie cookie) { }
            }
            """;
        var configuration = new Dictionary<string, string>
        {
            [PortcullisConventionKeys.SystemWebAllowedTypes] = "System.Web.HttpUtility, System.Web.HttpCookie",
        };

        var diagnostics = await AnalyzerTestHelper.GetDiagnosticsAsync(
            new SystemWebUsageAnalyzer(), configuration, ("src/Shop/Audit.cs", source));

        var hit = Assert.Single(diagnostics, d => d.Id == RuleId);
        Assert.Contains("'System.Web.HttpRequest'", hit.GetMessage());
    }

    [Fact]
    public async Task ReportsUsingSystemWebItselfOnceNothingInItIsAllowed()
    {
        const string source = """
            using System.Web;

            namespace Shop;

            public class Plain { }
            """;
        var configuration = new Dictionary<string, string>
        {
            [PortcullisConventionKeys.SystemWebAllowedTypes] = "",
        };

        var diagnostics = await AnalyzerTestHelper.GetDiagnosticsAsync(
            new SystemWebUsageAnalyzer(), configuration, ("src/Shop/Plain.cs", source));

        Assert.Equal("using System.Web;", Located(Assert.Single(diagnostics, d => d.Id == RuleId)));
    }

    // --- identity, not Portcullis's own declarations ---

    [Fact]
    public async Task RecognisesSystemWebDeclaredByAnyAssembly()
    {
        // The scan's declared legacy surface is not referenced here: the type comes from the
        // compilation itself, as it would from the real System.Web or from a shim.
        const string systemWeb = """
            namespace System.Web
            {
                public sealed class HttpRequest { }
            }
            """;
        const string consumer = """
            namespace Shop;

            public class Audit
            {
                public void Record(System.Web.HttpRequest request) { }
            }
            """;

        var diagnostics = await AnalyzerTestHelper.GetDiagnosticsAgainstCoreLibraryOnlyAsync(
            new SystemWebUsageAnalyzer(), ("src/Compat/SystemWeb.cs", systemWeb), ("src/Shop/Audit.cs", consumer));

        Assert.Equal("System.Web.HttpRequest", Located(Assert.Single(diagnostics, d => d.Id == RuleId)));
    }
}
