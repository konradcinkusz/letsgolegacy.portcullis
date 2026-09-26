using Portcullis.Rules;

namespace Portcullis.Engine.Tests.Rules;

public class RuleRegistryTests
{
    [Fact]
    public void All_ContainsTheSixExpressibleRulesPlusTheCoverageMetaRule()
    {
        Assert.Equal(7, RuleRegistry.All.Length);
        Assert.Contains(RuleRegistry.All, a => a is KernelBoundaryAnalyzer);
        Assert.Contains(RuleRegistry.All, a => a is ProgramManifestLayeringAnalyzer);
        Assert.Contains(RuleRegistry.All, a => a is ExtensibilityInheritanceAnalyzer);
        Assert.Contains(RuleRegistry.All, a => a is PersistencePortabilityAnalyzer);
        Assert.Contains(RuleRegistry.All, a => a is AntiCorruptionEdgeAnalyzer);
        Assert.Contains(RuleRegistry.All, a => a is ObservabilityBuildTimeAnalyzer);
        Assert.Contains(RuleRegistry.All, a => a is ConventionCoverageAnalyzer);
    }

    [Fact]
    public void EveryRuleId_FollowsThePortcullisNamingConventionAndIsAValidRoslynIdentifier()
    {
        foreach (var analyzer in RuleRegistry.All)
        {
            foreach (var descriptor in analyzer.SupportedDiagnostics)
            {
                Assert.StartsWith("PORTCULLIS_", descriptor.Id);
                Assert.All(descriptor.Id, c => Assert.True(char.IsLetterOrDigit(c) || c == '_'));
                // "Meta" is the one non-principle category: ConventionCoverageAnalyzer
                // reports on the gate's own coverage, not on a principle. Every other
                // descriptor must still carry a real architecture-standards principle id,
                // which is what this assertion exists to hold.
                Assert.Matches("^(P(1[0-5]|[1-9])|Meta)$", descriptor.Category);
            }
        }
    }
}
