using Portcullis.Rules;

namespace Portcullis.Engine.Tests.Rules;

public class RuleRegistryTests
{
    [Fact]
    public void All_ContainsTheSixExpressibleRulesTheFourMigrationRulesAndTheCoverageMetaRule()
    {
        Assert.Equal(11, RuleRegistry.All.Length);
        Assert.Contains(RuleRegistry.All, a => a is KernelBoundaryAnalyzer);
        Assert.Contains(RuleRegistry.All, a => a is ProgramManifestLayeringAnalyzer);
        Assert.Contains(RuleRegistry.All, a => a is ExtensibilityInheritanceAnalyzer);
        Assert.Contains(RuleRegistry.All, a => a is PersistencePortabilityAnalyzer);
        Assert.Contains(RuleRegistry.All, a => a is AntiCorruptionEdgeAnalyzer);
        Assert.Contains(RuleRegistry.All, a => a is ObservabilityBuildTimeAnalyzer);
        Assert.Contains(RuleRegistry.All, a => a is SystemWebUsageAnalyzer);
        Assert.Contains(RuleRegistry.All, a => a is HttpContextCurrentAnalyzer);
        Assert.Contains(RuleRegistry.All, a => a is SyncOverAsyncAnalyzer);
        Assert.Contains(RuleRegistry.All, a => a is ConfigurationManagerAnalyzer);
        Assert.Contains(RuleRegistry.All, a => a is ConventionCoverageAnalyzer);
    }

    /// <summary>
    /// The migration rule ids are a contract with the Second Key standards pack, which is
    /// built in another repository and cites them verbatim — so they are pinned here, not
    /// just checked for shape. Renaming one is a breaking change for that pack.
    /// </summary>
    [Fact]
    public void MigrationRules_ShipExactlyTheFourAgreedIdsInTheMigrationCategory()
    {
        var migration = RuleRegistry.All
            .SelectMany(a => a.SupportedDiagnostics)
            .Where(d => d.Category == "Migration")
            .Select(d => d.Id)
            .OrderBy(id => id, StringComparer.Ordinal)
            .ToArray();

        Assert.Equal(
            new[]
            {
                "PORTCULLIS_MIG_CONFIGURATION_MANAGER",
                "PORTCULLIS_MIG_HTTPCONTEXT_CURRENT",
                "PORTCULLIS_MIG_SYNC_OVER_ASYNC",
                "PORTCULLIS_MIG_SYSTEM_WEB",
            },
            migration);
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
                // Two non-principle categories exist: "Meta" (ConventionCoverageAnalyzer
                // reports on the gate's own coverage) and "Migration" (the PORTCULLIS_MIG_*
                // rules look for .NET Framework idioms, which no single principle names).
                // Every other descriptor must still carry a real architecture-standards
                // principle id, which is what this assertion exists to hold.
                Assert.Matches("^(P(1[0-5]|[1-9])|Meta|Migration)$", descriptor.Category);
            }
        }
    }
}
