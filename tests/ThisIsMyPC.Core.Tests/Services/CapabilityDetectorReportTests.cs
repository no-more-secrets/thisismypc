using ThisIsMyPC.Core.Modules;
using ThisIsMyPC.Core.Services;
using ThisIsMyPC.Core.Tests.Fakes;

namespace ThisIsMyPC.Core.Tests.Services;

public class CapabilityDetectorReportTests
{
    [Fact]
    public void Report_HasOneRowPerCapability_WithDisplayNames()
    {
        var detector = new CapabilityDetector(new FakeRegistryService());

        var report = detector.GetCapabilityReport();

        Assert.Equal(Enum.GetValues<SystemCapability>().Length, report.Count);
        Assert.All(report, r => Assert.False(string.IsNullOrWhiteSpace(r.DisplayName)));
        Assert.Equal(
            Enum.GetValues<SystemCapability>().OrderBy(c => c),
            report.Select(r => r.Capability).OrderBy(c => c));
    }

    [Fact]
    public void AlwaysPresentSubsystems_ReportAvailable()
    {
        var detector = new CapabilityDetector(new FakeRegistryService());

        foreach (var capability in new[]
        {
            SystemCapability.Registry, SystemCapability.Com,
            SystemCapability.Wmi, SystemCapability.NativeApi,
        })
        {
            Assert.True(detector.IsAvailable(capability));
        }
    }

    [Fact]
    public void UnavailableHardwareCapabilities_CarryRemediation()
    {
        var detector = new CapabilityDetector(new FakeRegistryService());

        var ddc = detector.GetAvailability(SystemCapability.DdcCi);
        Assert.False(ddc.IsAvailable);
        Assert.NotNull(ddc.Reason);

        // Fake registry: HWiNFO keys absent → unavailable with install remediation
        var hwinfo = detector.GetAvailability(SystemCapability.HwInfo);
        Assert.False(hwinfo.IsAvailable);
        Assert.Contains("HWiNFO", hwinfo.RemediationHint, StringComparison.Ordinal);
    }

    [Fact]
    public void HwInfo_DetectedViaRegistryPresence()
    {
        var registry = new FakeRegistryService();
        registry.AddKey(@"HKCU\Software\HWiNFO64");
        var detector = new CapabilityDetector(registry);

        var hwinfo = detector.GetAvailability(SystemCapability.HwInfo);

        Assert.True(hwinfo.IsAvailable);
        Assert.Contains("Shared Memory", hwinfo.RemediationHint, StringComparison.Ordinal);
    }

    [Fact]
    public void Availability_IsCachedPerSession()
    {
        var registry = new FakeRegistryService();
        var detector = new CapabilityDetector(registry);
        Assert.False(detector.IsAvailable(SystemCapability.HwInfo));

        // Key appears later — the cached answer deliberately stays (session-scoped)
        registry.AddKey(@"HKCU\Software\HWiNFO64");
        Assert.False(detector.IsAvailable(SystemCapability.HwInfo));
    }
}
