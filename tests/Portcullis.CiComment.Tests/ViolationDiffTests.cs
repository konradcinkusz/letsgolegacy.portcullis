using Portcullis.Engine.Model;

namespace Portcullis.CiComment.Tests;

public class ViolationDiffTests
{
    private static readonly Violation KernelCeiling = new(
        "PORTCULLIS-P2-KERNEL-LOC-CEILING", "src/ServiceDefaults/Extensions.cs", 812, "too long", "warning");

    private static readonly Violation ControllerDbContext = new(
        "PORTCULLIS-P9-CONTROLLER-NO-DBCONTEXT", "src/Adverts/Controllers/AdvertsController.cs", 47, "leaky", "error");

    private static readonly Violation VendorSdkLeak = new(
        "PORTCULLIS-P11-VENDOR-SDK-LEAK", "src/Identity/Services/EmailSender.cs", 22, "leaky sdk", "warning");

    [Fact]
    public void Compute_WithNoPreviousResult_TreatsEveryCurrentViolationAsNew()
    {
        var diff = ViolationDiff.Compute([KernelCeiling, ControllerDbContext], previous: null);

        Assert.Equal(2, diff.New.Count);
        Assert.Empty(diff.Resolved);
        Assert.Empty(diff.Unchanged);
    }

    [Fact]
    public void Compute_OneResolvedOneIntroduced_SplitsThemCorrectly()
    {
        IReadOnlyList<Violation> before = [ControllerDbContext, KernelCeiling];
        IReadOnlyList<Violation> after = [KernelCeiling, VendorSdkLeak];

        var diff = ViolationDiff.Compute(after, before);

        Assert.Equal(1, diff.New.Count);
        Assert.Equal(VendorSdkLeak, diff.New[0]);

        Assert.Equal(1, diff.Resolved.Count);
        Assert.Equal(ControllerDbContext, diff.Resolved[0]);

        Assert.Equal(1, diff.Unchanged.Count);
        Assert.Equal(KernelCeiling, diff.Unchanged[0]);
    }

    [Fact]
    public void Compute_IdenticalBeforeAndAfter_IsEntirelyUnchanged()
    {
        IReadOnlyList<Violation> violations = [KernelCeiling, ControllerDbContext];

        var diff = ViolationDiff.Compute(violations, violations);

        Assert.Empty(diff.New);
        Assert.Empty(diff.Resolved);
        Assert.Equal(2, diff.Unchanged.Count);
    }

    [Fact]
    public void Compute_EverythingResolved_NewIsEmptyAndResolvedMatchesPrevious()
    {
        IReadOnlyList<Violation> before = [KernelCeiling, ControllerDbContext];
        IReadOnlyList<Violation> after = [];

        var diff = ViolationDiff.Compute(after, before);

        Assert.Empty(diff.New);
        Assert.Equal(2, diff.Resolved.Count);
        Assert.Empty(diff.Unchanged);
    }

}
