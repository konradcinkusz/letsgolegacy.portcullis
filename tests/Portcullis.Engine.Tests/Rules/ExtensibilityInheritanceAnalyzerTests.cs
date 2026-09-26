using Portcullis.Rules;

namespace Portcullis.Engine.Tests.Rules;

public class ExtensibilityInheritanceAnalyzerTests
{
    [Fact]
    public async Task FiresWhenAClassInheritsFromAnotherClassDeclaredInTheSameSource()
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

        var diagnostics = await AnalyzerTestHelper.GetDiagnosticsAsync(
            new ExtensibilityInheritanceAnalyzer(),
            ("src/Demo/PaymentModule.cs", source));

        var hit = Assert.Single(diagnostics, d => d.Id == ExtensibilityInheritanceAnalyzer.CustomBaseClassRule.Id);
        Assert.Contains("PaymentModule", hit.GetMessage());
        Assert.Contains("ModuleBase", hit.GetMessage());
    }

    [Fact]
    public async Task DoesNotFireWhenAClassImplementsAnInterfaceInstead()
    {
        const string source = """
            namespace Demo;

            public interface IPaymentProvider
            {
            }

            public class PaymentModule : IPaymentProvider
            {
            }
            """;

        var diagnostics = await AnalyzerTestHelper.GetDiagnosticsAsync(
            new ExtensibilityInheritanceAnalyzer(),
            ("src/Demo/PaymentModule.cs", source));

        Assert.DoesNotContain(diagnostics, d => d.Id == ExtensibilityInheritanceAnalyzer.CustomBaseClassRule.Id);
    }

    [Fact]
    public async Task DoesNotFireWhenBaseClassIsAnUnresolvedFrameworkType()
    {
        // ControllerBase/DbContext never resolve at all in the scanner's single
        // compilation, which references no ASP.NET Core or EF Core (docs/BOOTSTRAP.md) —
        // this is the mechanism the rule relies on to avoid flagging legitimate
        // framework extension points. See ExtensibilityInheritanceAnalyzer's own doc
        // comment for the full reasoning.
        const string source = """
            namespace Demo;

            public class FooController : ControllerBase
            {
            }
            """;

        var diagnostics = await AnalyzerTestHelper.GetDiagnosticsAsync(
            new ExtensibilityInheritanceAnalyzer(),
            ("src/Demo/FooController.cs", source));

        Assert.DoesNotContain(diagnostics, d => d.Id == ExtensibilityInheritanceAnalyzer.CustomBaseClassRule.Id);
    }
}
