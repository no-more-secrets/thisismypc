using ThisIsMyPC.Core.Hardware;
using static ThisIsMyPC.Core.Tests.Hardware.HardwareFactsBuilder;

namespace ThisIsMyPC.Core.Tests.Hardware;

public sealed class GHelperSupportCatalogTests
{
    [Fact]
    public void VerifiedModels_StartsEmpty_UntilARealPassLands()
    {
        Assert.Empty(GHelperSupportCatalog.VerifiedModels);
    }

    [Fact]
    public void UnknownVendor_IsUnknown()
    {
        Assert.Equal(SupportVerdict.Unknown, GHelperSupportCatalog.Evaluate(UnknownMachine(), MachineFormFactor.Laptop));
    }

    [Fact]
    public void UnknownFormFactor_IsUnknown_EvenForAsus()
    {
        Assert.Equal(SupportVerdict.Unknown, GHelperSupportCatalog.Evaluate(AsusLaptop(), MachineFormFactor.Unknown));
    }

    [Fact]
    public void Desktop_IsUnsupported()
    {
        Assert.Equal(SupportVerdict.Unsupported, GHelperSupportCatalog.Evaluate(Desktop(), MachineFormFactor.Desktop));
    }

    [Fact]
    public void NonAsusLaptop_IsUnsupported()
    {
        Assert.Equal(SupportVerdict.Unsupported, GHelperSupportCatalog.Evaluate(OtherLaptop("HP"), MachineFormFactor.Laptop));
    }

    [Theory]
    [InlineData(true, SupportVerdict.Unverified)]
    [InlineData(false, SupportVerdict.Unsupported)]
    [InlineData(null, SupportVerdict.Unknown)]
    public void AsusLaptop_FollowsPlatformDriverEvidence(bool? atkacpi, SupportVerdict expected)
    {
        var facts = AsusLaptop() with { AsusPlatformDriverPresent = atkacpi };

        Assert.Equal(expected, GHelperSupportCatalog.Evaluate(facts, MachineFormFactor.Laptop));
    }

    [Theory]
    [InlineData(true, SupportVerdict.Unverified)]
    [InlineData(false, SupportVerdict.Unsupported)]
    [InlineData(null, SupportVerdict.Unknown)]
    public void RunningGHelper_IsNotSupportEvidence_DriverGateStillDecides(bool? atkacpi, SupportVerdict expected)
    {
        // Regression: a running process used to count as Verified, even with the driver absent.
        var facts = AsusLaptop(true, CompanionObservation.Running(CompanionApp.GHelper)) with { AsusPlatformDriverPresent = atkacpi };

        Assert.Equal(expected, GHelperSupportCatalog.Evaluate(facts, MachineFormFactor.Laptop));
    }

    [Fact]
    public void AsusLaptop_GHelperInstalledNotRunning_StaysUnverified()
    {
        var facts = AsusLaptop(true, CompanionObservation.Installed(CompanionApp.GHelper));

        Assert.Equal(SupportVerdict.Unverified, GHelperSupportCatalog.Evaluate(facts, MachineFormFactor.Laptop));
    }

    [Fact]
    public void Verified_RequiresBothVerifiedModelAndDriver()
    {
        // The catalog is empty, so no model can be Verified today; this pins the
        // driver gate for the day an entry is added: without the driver, still Unsupported.
        var facts = AsusLaptop(atkacpi: false);

        Assert.Equal(SupportVerdict.Unsupported, GHelperSupportCatalog.Evaluate(facts, MachineFormFactor.Laptop));
        Assert.DoesNotContain(facts.Identity.Model!, GHelperSupportCatalog.VerifiedModels, StringComparer.OrdinalIgnoreCase);
    }
}
