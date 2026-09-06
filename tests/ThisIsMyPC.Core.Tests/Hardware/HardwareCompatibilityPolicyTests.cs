using ThisIsMyPC.Core.Hardware;
using static ThisIsMyPC.Core.Tests.Hardware.HardwareFactsBuilder;

namespace ThisIsMyPC.Core.Tests.Hardware;

public sealed class HardwareCompatibilityPolicyTests
{
    private static HardwareTabDecision Decide(ObservedHardwareFacts facts, HardwareDomain domain, bool showAll = false)
        => HardwareCompatibilityPolicy.Decide(facts, new HardwareCompatibilityOptions(showAll)).For(domain);

    private static ObservedHardwareFacts LightingReady(params CompanionObservation[] extra)
        => Desktop("ASUS", [CompanionObservation.Running(CompanionApp.OpenRgb, HardwareDomain.Lighting), .. extra])
            with { OpenRgbServerReachable = true, OpenRgbDeviceCount = 2 };

    // ---- shape ----

    [Fact]
    public void Report_CoversEveryDomainOnce_AndKeepsIdentity()
    {
        var report = HardwareCompatibilityPolicy.Decide(AsusLaptop());

        Assert.Equal(Enum.GetValues<HardwareDomain>().Order(), report.Tabs.Select(t => t.Domain).Order());
        Assert.Equal(MachineVendor.Asus, report.Identity.Vendor);
        Assert.Equal(MachineFormFactor.Laptop, report.FormFactor.FormFactor);
        Assert.False(report.VisibilityOverrideActive);
        Assert.All(report.Tabs, t => Assert.False(string.IsNullOrWhiteSpace(t.Explanation)));
    }

    [Fact]
    public void EmptyFacts_EverythingUnknownOrPending_NoOperations_NoInstallOffers()
    {
        // Regression: Lighting and Cooling used to offer Install before detection ran.
        var report = HardwareCompatibilityPolicy.Decide(ObservedHardwareFacts.Empty);

        Assert.All(report.Tabs, t =>
        {
            Assert.Equal(HardwareOperations.None, t.Operations);
            Assert.False(t.LiveWritesAllowed);
            Assert.Null(t.Action);
            Assert.False(t.ControlsVisible);
        });
        Assert.Equal(HardwareAvailability.Unknown, report.For(HardwareDomain.SystemControl).Availability);
        Assert.Equal(HardwareAvailability.Unknown, report.For(HardwareDomain.Lighting).Availability);
        Assert.Equal(HardwareAvailability.Unknown, report.For(HardwareDomain.Cooling).Availability);
        Assert.Equal(HardwareAvailability.PendingVerification, report.For(HardwareDomain.Monitoring).Availability);
    }

    [Fact]
    public void Decide_IsDeterministic()
    {
        var facts = AsusLaptop(true, CompanionObservation.Running(CompanionApp.GHelper), CompanionObservation.Installed(CompanionApp.ArmouryCrate));

        var a = HardwareCompatibilityPolicy.Decide(facts);
        var b = HardwareCompatibilityPolicy.Decide(facts);

        foreach (var (x, y) in a.Tabs.Zip(b.Tabs))
        {
            Assert.Equal(x.Domain, y.Domain);
            Assert.Equal(x.Availability, y.Availability);
            Assert.Equal(x.Backend, y.Backend);
            Assert.Equal(x.Explanation, y.Explanation);
            Assert.Equal(x.Evidence, y.Evidence);
            Assert.Equal(x.ConflictNotes, y.ConflictNotes);
            Assert.Equal(x.Action, y.Action);
            Assert.Equal(x.Operations, y.Operations);
            Assert.Equal(x.ControlsVisible, y.ControlsVisible);
        }
    }

    // ---- operations vs availability ----

    [Fact]
    public void OnlyLighting_EverGetsWriteDevices()
    {
        var facts = LightingReady(CompanionObservation.Installed(CompanionApp.FanControl))
            with { SensorBackend = SensorBackendState.Ready };
        var report = HardwareCompatibilityPolicy.Decide(facts);

        Assert.True(report.For(HardwareDomain.Lighting).LiveWritesAllowed);
        Assert.Equal(HardwareAvailability.Available, report.For(HardwareDomain.Cooling).Availability);
        Assert.False(report.For(HardwareDomain.Cooling).LiveWritesAllowed);
        Assert.Equal(HardwareAvailability.Available, report.For(HardwareDomain.Monitoring).Availability);
        Assert.False(report.For(HardwareDomain.Monitoring).LiveWritesAllowed);
    }

    [Fact]
    public void Cooling_InstalledFanControl_IsAvailable_OpenOnly_NeverWrites()
    {
        // Regression: Available used to imply LiveWritesAllowed.
        var tab = Decide(Desktop("ASUS", CompanionObservation.Installed(CompanionApp.FanControl)), HardwareDomain.Cooling);

        Assert.Equal(HardwareAvailability.Available, tab.Availability);
        Assert.Equal(HardwareOperations.OpenCompanion, tab.Operations);
        Assert.False(tab.LiveWritesAllowed);
    }

    [Fact]
    public void Monitoring_Ready_IsReadOnly()
    {
        var tab = Decide(Desktop() with { SensorBackend = SensorBackendState.Ready }, HardwareDomain.Monitoring);

        Assert.Equal(HardwareAvailability.Available, tab.Availability);
        Assert.Equal(HardwareOperations.ReadSensors, tab.Operations);
        Assert.False(tab.LiveWritesAllowed);
    }

    // ---- unobserved companions ----

    [Theory]
    [InlineData(HardwareDomain.Lighting)]
    [InlineData(HardwareDomain.Cooling)]
    public void UnobservedCompanion_IsUnknown_WithoutInstallOffer(HardwareDomain domain)
    {
        var tab = Decide(Unobserved(Desktop()), domain);

        Assert.Equal(HardwareAvailability.Unknown, tab.Availability);
        Assert.Null(tab.Action);
        Assert.Equal(HardwareOperations.None, tab.Operations);
        Assert.Contains("not checked", tab.Explanation, StringComparison.Ordinal);
    }

    [Fact]
    public void ConfirmedAbsentCompanion_OffersInstall()
    {
        var tab = Decide(Desktop("ASUS", CompanionObservation.NotInstalled(CompanionApp.OpenRgb)), HardwareDomain.Lighting);

        Assert.Equal(HardwareAvailability.Unavailable, tab.Availability);
        Assert.Equal(new CompanionAction(CompanionActionKind.Install, CompanionApp.OpenRgb), tab.Action);
        Assert.Equal(HardwareOperations.InstallCompanion, tab.Operations);
    }

    [Fact]
    public void UnobservedInterferers_AddNoNotesOrEvidence()
    {
        var facts = LightingReady() with
        {
            Companions = [CompanionObservation.Running(CompanionApp.OpenRgb, HardwareDomain.Lighting)],
        };

        var tab = Decide(facts, HardwareDomain.Lighting);

        Assert.Equal(HardwareAvailability.Available, tab.Availability);
        Assert.Empty(tab.ConflictNotes);
        Assert.DoesNotContain(tab.Evidence, e => e.Contains("installed but not running", StringComparison.Ordinal));
    }

    // ---- System Control ----

    [Fact]
    public void SystemControl_Desktop_UnavailableWithDesktopExplanation()
    {
        var tab = Decide(Desktop(), HardwareDomain.SystemControl);

        Assert.Equal(HardwareAvailability.Unavailable, tab.Availability);
        Assert.Contains("desktop", tab.Explanation, StringComparison.OrdinalIgnoreCase);
        Assert.Null(tab.Action);
        Assert.False(tab.ControlsVisible);
    }

    [Fact]
    public void SystemControl_UnknownManufacturer_IsUnknownNotCompatible()
    {
        var tab = Decide(UnknownMachine(), HardwareDomain.SystemControl);

        Assert.Equal(HardwareAvailability.Unknown, tab.Availability);
        Assert.Contains("manufacturer", tab.Explanation, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(HardwareOperations.None, tab.Operations);
    }

    [Fact]
    public void SystemControl_AsusWithUnknownFormFactor_IsUnknown()
    {
        var facts = AsusLaptop() with { FormFactor = FormFactorEvidence.None };

        var tab = Decide(facts, HardwareDomain.SystemControl);

        Assert.Equal(HardwareAvailability.Unknown, tab.Availability);
        Assert.Contains("laptop", tab.Explanation, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void SystemControl_AsusLaptopWithAtkacpi_NotVerified_PendingWithoutInstallOffer()
    {
        var tab = Decide(AsusLaptop(atkacpi: true), HardwareDomain.SystemControl);

        Assert.Equal(HardwareAvailability.PendingVerification, tab.Availability);
        Assert.Equal(CompanionApp.GHelper, tab.Backend);
        Assert.Null(tab.Action);
        Assert.Equal(HardwareOperations.None, tab.Operations);
        Assert.Contains("not been verified", tab.Explanation, StringComparison.Ordinal);
    }

    [Fact]
    public void SystemControl_AsusLaptopPending_GHelperInstalled_OffersOpenOnly()
    {
        var tab = Decide(AsusLaptop(true, CompanionObservation.Installed(CompanionApp.GHelper)), HardwareDomain.SystemControl);

        Assert.Equal(HardwareAvailability.PendingVerification, tab.Availability);
        Assert.Equal(new CompanionAction(CompanionActionKind.Open, CompanionApp.GHelper), tab.Action);
        Assert.Equal(HardwareOperations.OpenCompanion, tab.Operations);
    }

    [Fact]
    public void SystemControl_AsusLaptopWithoutAtkacpi_Unavailable()
    {
        var tab = Decide(AsusLaptop(atkacpi: false), HardwareDomain.SystemControl);

        Assert.Equal(HardwareAvailability.Unavailable, tab.Availability);
        Assert.Contains("ATKACPI", tab.Explanation, StringComparison.Ordinal);
    }

    [Fact]
    public void SystemControl_AsusLaptopAtkacpiNotProbed_Unknown()
    {
        var facts = AsusLaptop() with { AsusPlatformDriverPresent = null };

        var tab = Decide(facts, HardwareDomain.SystemControl);

        Assert.Equal(HardwareAvailability.Unknown, tab.Availability);
    }

    [Fact]
    public void SystemControl_GHelperRunning_StaysPending_OpenOnly()
    {
        // Regression: a running G-Helper used to make the tab Available.
        var tab = Decide(AsusLaptop(true, CompanionObservation.Running(CompanionApp.GHelper)), HardwareDomain.SystemControl);

        Assert.Equal(HardwareAvailability.PendingVerification, tab.Availability);
        Assert.False(tab.LiveWritesAllowed);
        Assert.False(tab.ControlsVisible);
        Assert.Equal(new CompanionAction(CompanionActionKind.Open, CompanionApp.GHelper), tab.Action);
        Assert.Equal(HardwareOperations.OpenCompanion, tab.Operations);
        Assert.Contains(tab.Evidence, e => e.Contains("not taken as proof of support", StringComparison.Ordinal));
    }

    [Fact]
    public void SystemControl_GHelperRunningWithoutDriver_Unavailable()
    {
        var tab = Decide(AsusLaptop(false, CompanionObservation.Running(CompanionApp.GHelper)), HardwareDomain.SystemControl);

        Assert.Equal(HardwareAvailability.Unavailable, tab.Availability);
        Assert.Null(tab.Action);
    }

    [Fact]
    public void SystemControl_OtherVendorLaptop_UnavailableNamingTheVendor()
    {
        var tab = Decide(OtherLaptop("LENOVO"), HardwareDomain.SystemControl);

        Assert.Equal(HardwareAvailability.Unavailable, tab.Availability);
        Assert.Contains("LENOVO", tab.Explanation, StringComparison.Ordinal);
        Assert.Null(tab.Backend);
    }

    [Fact]
    public void SystemControl_ArmouryCrateInstalledOnly_IsEvidenceNotConflict()
    {
        var facts = AsusLaptop(true,
            CompanionObservation.Installed(CompanionApp.GHelper),
            CompanionObservation.Installed(CompanionApp.ArmouryCrate));

        var tab = Decide(facts, HardwareDomain.SystemControl);

        Assert.Equal(HardwareAvailability.PendingVerification, tab.Availability);
        Assert.Empty(tab.ConflictNotes);
        Assert.Contains(tab.Evidence, e => e.Contains("Armoury Crate is installed but not running", StringComparison.Ordinal));
    }

    [Fact]
    public void SystemControl_ArmouryCrateRunningWithoutObservedOwnership_IsAdvisoryOnly()
    {
        var facts = AsusLaptop(true,
            CompanionObservation.Installed(CompanionApp.GHelper),
            CompanionObservation.Running(CompanionApp.ArmouryCrate));

        var tab = Decide(facts, HardwareDomain.SystemControl);

        Assert.Equal(HardwareAvailability.PendingVerification, tab.Availability);
        Assert.Single(tab.ConflictNotes);
        Assert.Contains("ownership was not observed", tab.ConflictNotes[0], StringComparison.Ordinal);
    }

    [Fact]
    public void SystemControl_ArmouryCrateObservedOwningPlatform_IsConflict()
    {
        var facts = AsusLaptop(true,
            CompanionObservation.Installed(CompanionApp.GHelper),
            CompanionObservation.Running(CompanionApp.ArmouryCrate, HardwareDomain.SystemControl));

        var tab = Decide(facts, HardwareDomain.SystemControl);

        Assert.Equal(HardwareAvailability.Conflict, tab.Availability);
        Assert.Equal(HardwareOperations.None, tab.Operations);
        Assert.Null(tab.Action);
        Assert.Contains("Armoury Crate", tab.Explanation, StringComparison.Ordinal);
    }

    // ---- Lighting ----

    [Fact]
    public void Lighting_OpenRgbInstalledNotRunning_OffersOpen()
    {
        var tab = Decide(Desktop("ASUS", CompanionObservation.Installed(CompanionApp.OpenRgb)), HardwareDomain.Lighting);

        Assert.Equal(HardwareAvailability.Unavailable, tab.Availability);
        Assert.Equal(new CompanionAction(CompanionActionKind.Open, CompanionApp.OpenRgb), tab.Action);
        Assert.Equal(HardwareOperations.OpenCompanion, tab.Operations);
    }

    [Fact]
    public void Lighting_OpenRgbRunningServerNotProbed_Unknown()
    {
        var tab = Decide(Desktop("ASUS", CompanionObservation.Running(CompanionApp.OpenRgb)), HardwareDomain.Lighting);

        Assert.Equal(HardwareAvailability.Unknown, tab.Availability);
        Assert.False(tab.LiveWritesAllowed);
    }

    [Fact]
    public void Lighting_OpenRgbRunningServerDown_Unavailable()
    {
        var facts = Desktop("ASUS", CompanionObservation.Running(CompanionApp.OpenRgb)) with { OpenRgbServerReachable = false };

        var tab = Decide(facts, HardwareDomain.Lighting);

        Assert.Equal(HardwareAvailability.Unavailable, tab.Availability);
        Assert.Contains("SDK server", tab.Explanation, StringComparison.Ordinal);
    }

    [Fact]
    public void Lighting_ServerUpDeviceCountUnknown_IsUnknown_NoWrites()
    {
        // Regression: reachable server with an unread device list used to be Available.
        var facts = Desktop("ASUS", CompanionObservation.Running(CompanionApp.OpenRgb))
            with { OpenRgbServerReachable = true, OpenRgbDeviceCount = null };

        var tab = Decide(facts, HardwareDomain.Lighting);

        Assert.Equal(HardwareAvailability.Unknown, tab.Availability);
        Assert.False(tab.LiveWritesAllowed);
        Assert.Equal(HardwareOperations.None, tab.Operations);
        Assert.Contains("device list was not read", tab.Explanation, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(int.MinValue)]
    public void Lighting_ServerUpNegativeDeviceCount_IsUnknown_NoWrites(int count)
    {
        // Regression: a negative count used to fall through to Available.
        var facts = Desktop("ASUS", CompanionObservation.Running(CompanionApp.OpenRgb))
            with { OpenRgbServerReachable = true, OpenRgbDeviceCount = count };

        var tab = Decide(facts, HardwareDomain.Lighting);

        Assert.Equal(HardwareAvailability.Unknown, tab.Availability);
        Assert.False(tab.LiveWritesAllowed);
        Assert.Equal(HardwareOperations.None, tab.Operations);
        Assert.Null(tab.Action);
        Assert.Contains("invalid device count", tab.Explanation, StringComparison.Ordinal);
        Assert.Contains(tab.Evidence, e => e.Contains("device count invalid", StringComparison.Ordinal));
    }

    [Fact]
    public void Lighting_OpenRgbServerUpWithDevices_Available_WritesAllowed()
    {
        var facts = Desktop("ASUS", CompanionObservation.Running(CompanionApp.OpenRgb, HardwareDomain.Lighting))
            with { OpenRgbServerReachable = true, OpenRgbDeviceCount = 3 };

        var tab = Decide(facts, HardwareDomain.Lighting);

        Assert.Equal(HardwareAvailability.Available, tab.Availability);
        Assert.True(tab.LiveWritesAllowed);
        Assert.Equal(HardwareOperations.WriteDevices | HardwareOperations.OpenCompanion, tab.Operations);
        Assert.Empty(tab.ConflictNotes); // the backend owning its own domain is not a conflict
        Assert.Contains(tab.Evidence, e => e.Contains("device count: 3", StringComparison.Ordinal));
    }

    [Fact]
    public void Lighting_OpenRgbServerUpNoDevices_Unavailable()
    {
        var facts = Desktop("ASUS", CompanionObservation.Running(CompanionApp.OpenRgb))
            with { OpenRgbServerReachable = true, OpenRgbDeviceCount = 0 };

        var tab = Decide(facts, HardwareDomain.Lighting);

        Assert.Equal(HardwareAvailability.Unavailable, tab.Availability);
        Assert.Contains("no controllable", tab.Explanation, StringComparison.Ordinal);
    }

    [Fact]
    public void Lighting_LaptopIsNotExcluded()
    {
        var facts = AsusLaptop(true, CompanionObservation.Running(CompanionApp.OpenRgb))
            with { OpenRgbServerReachable = true, OpenRgbDeviceCount = 1 };

        var tab = Decide(facts, HardwareDomain.Lighting);

        Assert.Equal(HardwareAvailability.Available, tab.Availability);
        Assert.Contains(tab.Evidence, e => e.Contains("laptops are not excluded", StringComparison.Ordinal));
    }

    [Fact]
    public void Lighting_SignalRgbObservedOwningLighting_Conflict()
    {
        var tab = Decide(LightingReady(CompanionObservation.Running(CompanionApp.SignalRgb, HardwareDomain.Lighting)), HardwareDomain.Lighting);

        Assert.Equal(HardwareAvailability.Conflict, tab.Availability);
        Assert.False(tab.LiveWritesAllowed);
        Assert.Equal(HardwareOperations.None, tab.Operations);
        Assert.Contains("SignalRGB", tab.Explanation, StringComparison.Ordinal);
    }

    [Fact]
    public void Lighting_SignalRgbRunningWithoutOwnership_AdvisoryKeepsAvailable()
    {
        var tab = Decide(LightingReady(CompanionObservation.Running(CompanionApp.SignalRgb)), HardwareDomain.Lighting);

        Assert.Equal(HardwareAvailability.Available, tab.Availability);
        Assert.Single(tab.ConflictNotes);
    }

    // ---- Cooling ----

    [Fact]
    public void Cooling_FanControlConfirmedAbsent_OffersInstall()
    {
        var tab = Decide(Desktop(), HardwareDomain.Cooling);

        Assert.Equal(HardwareAvailability.Unavailable, tab.Availability);
        Assert.Equal(new CompanionAction(CompanionActionKind.Install, CompanionApp.FanControl), tab.Action);
        Assert.Equal(HardwareOperations.InstallCompanion, tab.Operations);
    }

    [Fact]
    public void Cooling_FanControlRunning_ExplanationDoesNotClaimOwnership()
    {
        // Regression: the copy used to say a running FanControl "owns the fan curves".
        var tab = Decide(Desktop("ASUS", CompanionObservation.Running(CompanionApp.FanControl)), HardwareDomain.Cooling);

        Assert.Equal(HardwareAvailability.Available, tab.Availability);
        Assert.DoesNotContain("own", tab.Explanation, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(tab.Evidence, e => e.Contains("configuration loaded", StringComparison.Ordinal));
        Assert.Equal(new CompanionAction(CompanionActionKind.Open, CompanionApp.FanControl), tab.Action);
    }

    [Fact]
    public void Cooling_FanControlWithObservedOwnership_RecordsItAsEvidence()
    {
        var tab = Decide(Desktop("ASUS", CompanionObservation.Running(CompanionApp.FanControl, HardwareDomain.Cooling)), HardwareDomain.Cooling);

        Assert.Equal(HardwareAvailability.Available, tab.Availability);
        Assert.Contains(tab.Evidence, e => e.Contains("configuration loaded", StringComparison.Ordinal));
        Assert.Empty(tab.ConflictNotes); // the backend itself is never a conflict
    }

    [Fact]
    public void Cooling_GHelperObservedOwningFans_Conflict()
    {
        var facts = AsusLaptop(true,
            CompanionObservation.Installed(CompanionApp.FanControl),
            CompanionObservation.Running(CompanionApp.GHelper, HardwareDomain.SystemControl, HardwareDomain.Cooling));

        var tab = Decide(facts, HardwareDomain.Cooling);

        Assert.Equal(HardwareAvailability.Conflict, tab.Availability);
        Assert.Contains("G-Helper", tab.Explanation, StringComparison.Ordinal);
    }

    [Fact]
    public void Cooling_GHelperRunningWithoutOwnership_AdvisoryOnly()
    {
        var facts = AsusLaptop(true,
            CompanionObservation.Installed(CompanionApp.FanControl),
            CompanionObservation.Running(CompanionApp.GHelper));

        var tab = Decide(facts, HardwareDomain.Cooling);

        Assert.Equal(HardwareAvailability.Available, tab.Availability);
        Assert.Single(tab.ConflictNotes);
        Assert.Contains(tab.Evidence, e => e.Contains("System Control", StringComparison.Ordinal));
    }

    // ---- Monitoring ----

    [Theory]
    [InlineData(SensorBackendState.NotIntegrated, HardwareAvailability.PendingVerification, HardwareOperations.None)]
    [InlineData(SensorBackendState.DriverMissing, HardwareAvailability.Unavailable, HardwareOperations.None)]
    [InlineData(SensorBackendState.Ready, HardwareAvailability.Available, HardwareOperations.ReadSensors)]
    public void Monitoring_FollowsSensorBackendState(SensorBackendState state, HardwareAvailability expected, HardwareOperations operations)
    {
        var facts = Desktop() with { SensorBackend = state };

        var tab = Decide(facts, HardwareDomain.Monitoring);

        Assert.Equal(expected, tab.Availability);
        Assert.Equal(operations, tab.Operations);
        Assert.Equal(CompanionApp.LibreHardwareMonitor, tab.Backend);
    }

    [Fact]
    public void Monitoring_HwInfoRunning_IsNotAConflict()
    {
        var facts = Desktop("ASUS", CompanionObservation.Running(CompanionApp.HwInfo)) with { SensorBackend = SensorBackendState.Ready };

        var tab = Decide(facts, HardwareDomain.Monitoring);

        Assert.Equal(HardwareAvailability.Available, tab.Availability);
        Assert.Empty(tab.ConflictNotes);
    }

    // ---- debug visibility override ----

    [Fact]
    public void Override_RevealsControls_ButGrantsNoOperations()
    {
        var report = HardwareCompatibilityPolicy.Decide(UnknownMachine(), new HardwareCompatibilityOptions(ShowAllControls: true));

        Assert.True(report.VisibilityOverrideActive);
        Assert.All(report.Tabs, t =>
        {
            Assert.True(t.ControlsVisible);
            // Confirmed-absent companions may still offer Install; hardware operations never appear.
            Assert.False(t.Operations.HasFlag(HardwareOperations.WriteDevices));
            Assert.False(t.Operations.HasFlag(HardwareOperations.ReadSensors));
            Assert.False(t.LiveWritesAllowed);
            Assert.NotEqual(HardwareAvailability.Available, t.Availability);
        });
    }

    [Fact]
    public void Override_RevealsControls_OnEmptyFacts_WithoutInstallOffers()
    {
        var report = HardwareCompatibilityPolicy.Decide(ObservedHardwareFacts.Empty, new HardwareCompatibilityOptions(true));

        Assert.All(report.Tabs, t =>
        {
            Assert.True(t.ControlsVisible);
            Assert.Null(t.Action);
            Assert.Equal(HardwareOperations.None, t.Operations);
        });
    }

    public static TheoryData<string, ObservedHardwareFacts> OverrideScenarios => new()
    {
        { "lighting conflict", LightingReady(CompanionObservation.Running(CompanionApp.SignalRgb, HardwareDomain.Lighting)) },
        { "lighting available", LightingReady() },
        { "lighting device count unknown", Desktop("ASUS", CompanionObservation.Running(CompanionApp.OpenRgb)) with { OpenRgbServerReachable = true } },
        { "lighting device count invalid", Desktop("ASUS", CompanionObservation.Running(CompanionApp.OpenRgb)) with { OpenRgbServerReachable = true, OpenRgbDeviceCount = -1 } },
        { "role-only laptop guess", AsusLaptop() with { FormFactor = new FormFactorEvidence { PlatformRole = PlatformRole.Mobile, HasSystemBattery = true } } },
        { "pending asus laptop", AsusLaptop(true, CompanionObservation.Running(CompanionApp.GHelper)) },
        { "cooling installed", Desktop("ASUS", CompanionObservation.Installed(CompanionApp.FanControl)) },
        { "monitoring ready", Desktop() with { SensorBackend = SensorBackendState.Ready } },
        { "unobserved", Unobserved(Desktop()) },
        { "empty", ObservedHardwareFacts.Empty },
    };

    [Theory]
    [MemberData(nameof(OverrideScenarios))]
    public void Override_ChangesOnlyControlsVisible(string scenario, ObservedHardwareFacts facts)
    {
        Assert.NotNull(scenario);
        var plain = HardwareCompatibilityPolicy.Decide(facts).Tabs;
        var overridden = HardwareCompatibilityPolicy.Decide(facts, new HardwareCompatibilityOptions(true)).Tabs;

        foreach (var (a, b) in plain.Zip(overridden))
        {
            Assert.Equal(a.Availability, b.Availability);
            Assert.Equal(a.Operations, b.Operations);
            Assert.Equal(a.LiveWritesAllowed, b.LiveWritesAllowed);
            Assert.Equal(a.ConflictNotes, b.ConflictNotes);
            Assert.Equal(a.Evidence, b.Evidence);
            Assert.Equal(a.Action, b.Action);
            Assert.Equal(a.Explanation, b.Explanation);
            Assert.Equal(a.Backend, b.Backend);
            Assert.True(b.ControlsVisible);
        }
    }

    // ---- bridge ----

    [Fact]
    public void ToModuleAvailability_CarriesExplanationAndActionHint()
    {
        var tab = Decide(Desktop(), HardwareDomain.Lighting);

        var availability = tab.ToModuleAvailability();

        Assert.False(availability.IsAvailable);
        Assert.Equal(tab.Explanation, availability.Reason);
        Assert.Equal("Install OpenRGB from Software.", availability.RemediationHint);
    }

    [Fact]
    public void ToModuleAvailability_AvailableHasNoReason_AndDoesNotMeanWritable()
    {
        var facts = Desktop("ASUS", CompanionObservation.Installed(CompanionApp.FanControl));
        var tab = Decide(facts, HardwareDomain.Cooling);

        var availability = tab.ToModuleAvailability();

        Assert.True(availability.IsAvailable);
        Assert.Null(availability.Reason);
        Assert.Equal("Open FanControl.", availability.RemediationHint);
        Assert.False(tab.LiveWritesAllowed);
    }
}
