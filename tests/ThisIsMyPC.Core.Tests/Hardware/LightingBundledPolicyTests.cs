using ThisIsMyPC.Core.Hardware;
using ThisIsMyPC.Core.Hardware.Detection;
using ThisIsMyPC.Core.Tests.Fakes;

namespace ThisIsMyPC.Core.Tests.Hardware;

public sealed class LightingBundledPolicyTests
{
    [Fact]
    public void BundledCopy_NotRunning_AsksForTheServiceStart()
    {
        var facts = HardwareFactsBuilder.Desktop(companions: CompanionObservation.Installed(CompanionApp.OpenRgb)) with { OpenRgbBundled = true };

        var lighting = HardwareCompatibilityPolicy.Decide(facts).For(HardwareDomain.Lighting);

        Assert.Equal(HardwareAvailability.Unavailable, lighting.Availability);
        Assert.Equal(new CompanionAction(CompanionActionKind.StartService, CompanionApp.OpenRgb), lighting.Action);
        Assert.Equal(HardwareOperations.OpenCompanion, lighting.Operations);
        Assert.Equal("The lighting service is not running. It starts when this page opens.", lighting.Explanation);
        Assert.Contains(lighting.Evidence, e => e.StartsWith("A bundled OpenRGB", StringComparison.Ordinal));
        Assert.False(lighting.LiveWritesAllowed);
        Assert.Equal("Start the bundled OpenRGB service.", lighting.ToModuleAvailability().RemediationHint);
    }

    [Fact]
    public void BundledCopy_UsersOwnOpenRgbRunningWithoutTheServer_OffersOpenNotStart()
    {
        // Starting the bundled server beside a running OpenRGB would fight it
        // for the devices, so the action stays Open on the running copy.
        var facts = HardwareFactsBuilder.Desktop(companions: CompanionObservation.Running(CompanionApp.OpenRgb))
            with { OpenRgbBundled = true, OpenRgbServerReachable = false };

        var lighting = HardwareCompatibilityPolicy.Decide(facts).For(HardwareDomain.Lighting);

        Assert.Equal(HardwareAvailability.Unavailable, lighting.Availability);
        Assert.Equal(CompanionActionKind.Open, lighting.Action?.Kind);
        Assert.Contains("SDK server", lighting.Explanation, StringComparison.Ordinal);
    }

    [Fact]
    public void BundledCopy_ServerAnsweringWithDevices_HasNoButton()
    {
        var facts = HardwareFactsBuilder.Desktop(companions: CompanionObservation.Running(CompanionApp.OpenRgb, HardwareDomain.Lighting))
            with { OpenRgbBundled = true, OpenRgbServerReachable = true, OpenRgbDeviceCount = 2 };

        var lighting = HardwareCompatibilityPolicy.Decide(facts).For(HardwareDomain.Lighting);

        Assert.Equal(HardwareAvailability.Available, lighting.Availability);
        Assert.Null(lighting.Action);
        Assert.True(lighting.LiveWritesAllowed);
    }

    [Fact]
    public void NoBundledCopy_KeepsTheUserInstallCopyAndOpen()
    {
        var facts = HardwareFactsBuilder.Desktop(companions: CompanionObservation.Installed(CompanionApp.OpenRgb));

        var lighting = HardwareCompatibilityPolicy.Decide(facts).For(HardwareDomain.Lighting);

        Assert.Equal(CompanionActionKind.Open, lighting.Action?.Kind);
        Assert.Equal("OpenRGB is installed but not running. Start it with its SDK server enabled.", lighting.Explanation);
        Assert.DoesNotContain(lighting.Evidence, e => e.StartsWith("A bundled OpenRGB", StringComparison.Ordinal));
    }

    [Fact]
    public void Detector_CountsTheBundledCopyAsInstalled_AndLaunchesIt()
    {
        var env = new FakeHardwareProbeEnvironment();
        env.AddFile(@"C:\Program Files\ThisIsMyPC\companions\OpenRGB\OpenRGB.exe");
        env.Bundled[CompanionApp.OpenRgb] = @"C:\Program Files\ThisIsMyPC\companions\OpenRGB\OpenRGB.exe";
        var detector = new CompanionDetector(new FakeRegistryService(), env);

        var openRgb = detector.DetectAll(false, null).Single(d => d.Observation.App == CompanionApp.OpenRgb);

        Assert.True(openRgb.Observation.IsInstalled);
        Assert.False(openRgb.Observation.IsRunning);
        Assert.Equal(@"C:\Program Files\ThisIsMyPC\companions\OpenRGB\OpenRGB.exe", openRgb.LaunchPath);
        Assert.Contains(openRgb.Notes, n => n.StartsWith("OpenRGB: bundled copy at", StringComparison.Ordinal));
    }

    [Fact]
    public void Detector_RunningProcessPath_WinsOverTheBundledCopy()
    {
        var env = new FakeHardwareProbeEnvironment();
        env.AddFile(@"C:\Program Files\ThisIsMyPC\companions\OpenRGB\OpenRGB.exe");
        env.Bundled[CompanionApp.OpenRgb] = @"C:\Program Files\ThisIsMyPC\companions\OpenRGB\OpenRGB.exe";
        env.AddFile(@"C:\Program Files\OpenRGB\OpenRGB.exe");
        env.Processes["OpenRGB"] = @"C:\Program Files\OpenRGB\OpenRGB.exe";
        var detector = new CompanionDetector(new FakeRegistryService(), env);

        var openRgb = detector.DetectAll(false, new OpenRgbProbeResult(true, 1, "answered")).Single(d => d.Observation.App == CompanionApp.OpenRgb);

        Assert.True(openRgb.Observation.IsRunning);
        Assert.Equal(@"C:\Program Files\OpenRGB\OpenRGB.exe", openRgb.LaunchPath);
    }
}
