using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Portcullis.Rules;

namespace Portcullis.Engine.Tests;

/// <summary>
/// The candidate of a migration is modern C#: a .NET 10 project compiles C# 14 by default,
/// and the scanner parses with the newest language its own Roslyn knows. When that Roslyn
/// is older than the candidate's language, a C# 14 construct is a syntax error, and what
/// sits inside it is analysed from an error-recovered tree — or not at all, which is a
/// finding the gate silently misses.
/// </summary>
public class ScannerLanguageVersionTests
{
    private const string Candidate = """
        using System.Threading.Tasks;

        namespace Shop;

        public static class PendingOrders
        {
            extension(Task<string> pending)
            {
                public string Now => pending.Result;
            }
        }

        public class Basket
        {
            public string Name
            {
                get => field;
                set => field = value ?? string.Empty;
            }
        }
        """;

    [Fact]
    public void TheScannersRoslyn_ParsesCsharp14_WithoutSyntaxErrors()
    {
        var tree = CSharpSyntaxTree.ParseText(Candidate);

        Assert.DoesNotContain(tree.GetDiagnostics(), d => d.Severity == DiagnosticSeverity.Error);
        Assert.True(((CSharpParseOptions)tree.Options).LanguageVersion >= LanguageVersion.CSharp14);
    }

    [Fact]
    public async Task ScanAsync_SyncOverAsyncInsideAnExtensionBlock_IsFound()
    {
        var dir = Directory.CreateTempSubdirectory("portcullis-csharp14-scan-");
        try
        {
            await File.WriteAllTextAsync(Path.Combine(dir.FullName, "PendingOrders.cs"), Candidate);

            var result = await Scanner.ScanAsync(dir.FullName);

            Assert.Contains(result.Violations, v => v.RuleId == SyncOverAsyncAnalyzer.SyncOverAsyncRule.Id && v.Line == 9);
        }
        finally
        {
            dir.Delete(recursive: true);
        }
    }
}
