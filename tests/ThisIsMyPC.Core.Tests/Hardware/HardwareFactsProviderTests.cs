using ThisIsMyPC.Core.Hardware;
using ThisIsMyPC.Core.Hardware.Detection;
using ThisIsMyPC.Core.Tests.Fakes;

namespace ThisIsMyPC.Core.Tests.Hardware;

public sealed class HardwareFactsProviderTests
{
    /// <summary>The shared inventory as a script: what it answers, how often it was asked, and whether it throws.</summary>
    private sealed class InventoryFake : IHardwareDetectionService
    {
        public HardwareSnapshot Snapshot { get; set; } = SamsDesktop();
        public int Gets { get; private set; }
        public int Refreshes { get; private set; }
        public Exception? Throws { get; set; }

        public Task<HardwareSnapshot> GetSnapshotAsync(CancellationToken cancellationToken = default)
        {
            Gets++;
            return Throws is { } ex ? Task.FromException<HardwareSnapshot>(ex) : Task.FromResult(Snapshot);
        }

        public Task<HardwareSnapshot> RefreshAsync(CancellationToken cancellationToken = default)
        {
            Refreshes++;
            return Throws is { } ex ? Task.FromException<HardwareSnapshot>(ex) : Task.FromResult(Snapshot);
        }
    }

    private static HardwareSnapshot SamsDesktop(bool atkacpi = false, params CompanionObservation[] companions) => new()
    {
        ObservedAt = DateTimeOffset.UtcNow,
        Facts = new ObservedHardwareFacts
        {
            Identity = MachineIdentity.From("ASUSTeK COMPUTER INC.", "ROG STRIX X670E-E GAMING WIFI"),
            FormFactor = new FormFactorEvidence
            {
                SmbiosChassisTypes = [3],
                PlatformRole = PlatformRole.Desktop,
                HasSystemBattery = false,
            },
            AsusPlatformDriverPresent = atkacpi,
            Companions = companions,
        },
        Firmware = new FirmwareInventory { ChassisTypes = [3], BoardProduct = "ROG STRIX X670E-E GAMING WIFI" },
        Issues = ["Present device enumeration was incomplete."],
    };

    [Fact]
    public async Task Detect_LayersCompanionsAndThePanelOverTheSharedInventory()
    {
        var env = new FakeHardwareProbeEnvironment();
        env.AddFile(@"C:\Program Files (x86)\FanControl_199\FanControl.exe");
        env.Processes["FanControl"] = @"C:\Program Files (x86)\FanControl_199\FanControl.exe";
        var inventory = new InventoryFake();
        using var provider = new HardwareFactsProvider(inventory, new FakeRegistryService(), env, internalPanelProbe: () => false);

        var snapshot = await provider.GetAsync();
        var facts = snapshot.Facts;

        // Shared facts pass through untouched.
        Assert.Equal(MachineVendor.Asus, facts.Identity.Vendor);
        Assert.Equal("ROG STRIX X670E-E GAMING WIFI", facts.Identity.Model);
        Assert.Equal([3], facts.FormFactor.SmbiosChassisTypes);
        Assert.Equal(PlatformRole.Desktop, facts.FormFactor.PlatformRole);
        Assert.False(facts.FormFactor.HasSystemBattery);
        Assert.False(facts.AsusPlatformDriverPresent);
        Assert.Same(inventory.Snapshot, snapshot.Inventory);

        // The module layer adds the rest.
        Assert.False(facts.FormFactor.HasInternalDisplayPanel);
        Assert.Equal(Enum.GetValues<CompanionApp>().Length, facts.Companions.Count);
        Assert.True(facts.IsRunning(CompanionApp.FanControl));
        Assert.False(facts.IsInstalled(CompanionApp.OpenRgb));
        Assert.Null(facts.OpenRgbServerReachable);
        Assert.Equal(SensorBackendState.NotIntegrated, facts.SensorBackend);
        Assert.Equal(@"C:\Program Files (x86)\FanControl_199\FanControl.exe", snapshot.LaunchPathOf(CompanionApp.FanControl));
        Assert.Contains("SMBIOS chassis types: 3.", snapshot.Notes);
        Assert.Contains("Shared hardware inventory: Present device enumeration was incomplete.", snapshot.Notes);

        // And the policy reads it the way the Cooling tab expects.
        var report = HardwareCompatibilityPolicy.Decide(facts);
        Assert.Equal(HardwareAvailability.Available, report.For(HardwareDomain.Cooling).Availability);
        Assert.Equal(HardwareAvailability.Unavailable, report.For(HardwareDomain.SystemControl).Availability);
        Assert.Equal(MachineFormFactor.Desktop, report.FormFactor.FormFactor);
    }

    [Fact]
    public async Task Detect_ConfirmsAbsences_AndKeepsTheInventorysPositiveSightings()
    {
        // The shared inventory records only positive hits (FanControl running here,
        // seen by process name), never absences. The module layer turns the rest
        // into NotInstalled so the policy can offer an install, and keeps the
        // sighting it could not reproduce itself, with a note saying where it came from.
        var inventory = new InventoryFake { Snapshot = SamsDesktop(false, CompanionObservation.Running(CompanionApp.FanControl)) };
        var env = new FakeHardwareProbeEnvironment();
        using var provider = new HardwareFactsProvider(inventory, new FakeRegistryService(), env);

        var snapshot = await provider.GetAsync();
        var facts = snapshot.Facts;

        Assert.False(facts.IsInstalled(CompanionApp.OpenRgb));
        Assert.Equal(CompanionActionKind.Install, HardwareCompatibilityPolicy.Decide(facts).For(HardwareDomain.Lighting).Action?.Kind);
        Assert.True(facts.IsRunning(CompanionApp.FanControl));
        Assert.Null(snapshot.LaunchPathOf(CompanionApp.FanControl));
        Assert.Empty(facts.Companion(CompanionApp.FanControl)!.ObservedOwnership);
        Assert.Contains("FanControl: the shared inventory saw it running.", snapshot.Notes);
    }

    [Fact]
    public async Task Detect_ProbesTheOpenRgbServer_OnlyWhileOpenRgbRuns()
    {
        var idle = new FakeHardwareProbeEnvironment();
        using var idleProvider = new HardwareFactsProvider(new InventoryFake(), new FakeRegistryService(), idle);
        var idleFacts = (await idleProvider.GetAsync()).Facts;

        var running = new FakeHardwareProbeEnvironment { OpenRgb = new OpenRgbProbeResult(true, 2, "answered") };
        running.Processes["OpenRGB"] = @"C:\Program Files\OpenRGB\OpenRGB.exe";
        running.AddFile(@"C:\Program Files\OpenRGB\OpenRGB.exe");
        using var runningProvider = new HardwareFactsProvider(new InventoryFake(), new FakeRegistryService(), running);
        var runningFacts = (await runningProvider.GetAsync()).Facts;

        Assert.Equal(0, idle.OpenRgbProbes);
        Assert.Null(idleFacts.OpenRgbServerReachable);
        Assert.Equal(1, running.OpenRgbProbes);
        Assert.True(runningFacts.OpenRgbServerReachable);
        Assert.Equal(2, runningFacts.OpenRgbDeviceCount);
        Assert.Equal(HardwareAvailability.Available, HardwareCompatibilityPolicy.Decide(runningFacts).For(HardwareDomain.Lighting).Availability);
    }

    [Fact]
    public async Task Detect_ServerNotAnswering_LeavesTheDeviceCountNull()
    {
        var env = new FakeHardwareProbeEnvironment { OpenRgb = OpenRgbProbeResult.Unreachable("ConnectionRefused") };
        env.Processes["OpenRGB"] = string.Empty;
        using var provider = new HardwareFactsProvider(new InventoryFake(), new FakeRegistryService(), env);

        var facts = (await provider.GetAsync()).Facts;

        Assert.False(facts.OpenRgbServerReachable);
        Assert.Null(facts.OpenRgbDeviceCount);
        Assert.Equal(HardwareAvailability.Unavailable, HardwareCompatibilityPolicy.Decide(facts).For(HardwareDomain.Lighting).Availability);
    }

    [Fact]
    public async Task GetAsync_CachesUntilRefresh_RefreshAsksTheInventoryToRefresh_AndRaisesChanged()
    {
        var inventory = new InventoryFake();
        using var provider = new HardwareFactsProvider(inventory, new FakeRegistryService(), new FakeHardwareProbeEnvironment());
        var changes = 0;
        provider.Changed += (_, _) => changes++;

        Assert.Null(provider.Current);
        var first = await provider.GetAsync();
        var second = await provider.GetAsync();
        inventory.Snapshot = SamsDesktop(atkacpi: true);
        var third = await provider.GetAsync(refresh: true);

        Assert.Same(first, second);
        Assert.NotSame(first, third);
        Assert.True(third.Facts.AsusPlatformDriverPresent);
        Assert.Equal(1, inventory.Gets);
        Assert.Equal(1, inventory.Refreshes);
        Assert.Equal(2, changes);
        Assert.Same(third, provider.Current);
    }

    [Fact]
    public async Task Detect_InventoryFailure_BecomesUnknownFactsWithANote()
    {
        var inventory = new InventoryFake { Throws = new InvalidOperationException("firmware") };
        using var provider = new HardwareFactsProvider(
            inventory, new FakeRegistryService(), new FakeHardwareProbeEnvironment(),
            internalPanelProbe: () => throw new InvalidOperationException("panel"));

        var snapshot = await provider.GetAsync();

        Assert.Null(snapshot.Facts.FormFactor.SmbiosChassisTypes);
        Assert.Null(snapshot.Facts.FormFactor.HasInternalDisplayPanel);
        Assert.Equal(MachineVendor.Unknown, snapshot.Facts.Identity.Vendor);
        Assert.Contains(snapshot.Notes, n => n.StartsWith("Shared hardware inventory: read failed", StringComparison.Ordinal));
        Assert.Contains(snapshot.Notes, n => n.StartsWith("internal panel: probe failed", StringComparison.Ordinal));
        // Companions were still checked, so absences are confirmed even without firmware.
        Assert.False(snapshot.Facts.IsInstalled(CompanionApp.FanControl));
        Assert.Equal(HardwareAvailability.Unknown, HardwareCompatibilityPolicy.Decide(snapshot.Facts).For(HardwareDomain.SystemControl).Availability);
    }

    [Fact]
    public async Task GetAsync_Cancellation_Propagates()
    {
        var inventory = new InventoryFake { Throws = new OperationCanceledException() };
        using var provider = new HardwareFactsProvider(inventory, new FakeRegistryService(), new FakeHardwareProbeEnvironment());

        await Assert.ThrowsAsync<OperationCanceledException>(() => provider.GetAsync());
        Assert.Null(provider.Current);
    }
}
