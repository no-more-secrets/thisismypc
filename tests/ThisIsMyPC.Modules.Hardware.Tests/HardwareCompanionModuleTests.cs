using ThisIsMyPC.Core.Changes;
using ThisIsMyPC.Core.Hardware;
using ThisIsMyPC.Core.Modules;
using ThisIsMyPC.Core.Settings;
using ThisIsMyPC.Modules.Hardware.Models;
using ThisIsMyPC.Modules.Hardware.Tests.Fakes;

namespace ThisIsMyPC.Modules.Hardware.Tests;

public sealed class HardwareCompanionModuleTests
{
    private static readonly FormFactorEvidence DesktopEvidence = new()
    {
        SmbiosChassisTypes = [3],
        PlatformRole = PlatformRole.Desktop,
        HasSystemBattery = false,
    };

    private static ObservedHardwareFacts Desktop(params CompanionObservation[] companions)
    {
        var list = new List<CompanionObservation>(companions);
        foreach (var app in Enum.GetValues<CompanionApp>())
        {
            if (list.All(o => o.App != app))
                list.Add(CompanionObservation.NotInstalled(app));
        }
        return new ObservedHardwareFacts
        {
            Identity = MachineIdentity.From("ASUSTeK COMPUTER INC.", "ROG STRIX X670E-E GAMING WIFI"),
            FormFactor = DesktopEvidence,
            AsusPlatformDriverPresent = false,
            Companions = list,
        };
    }

    [Fact]
    public void EveryTab_IsAlwaysAvailableInTheSidebar()
    {
        var facts = new FakeHardwareFactsProvider(ObservedHardwareFacts.Empty);
        IModule[] modules =
        [
            new SystemControlModule(facts), new LightingModule(facts), new CoolingModule(facts), new MonitoringModule(facts),
        ];

        foreach (var module in modules)
        {
            Assert.True(module.CheckAvailabilityAsync().Result.IsAvailable, module.Info.Name);
            Assert.Equal(ModuleGroup.Hardware, module.Info.Group);
        }
        Assert.Equal(4, modules.Select(m => m.Info.LoadOrder).Distinct().Count());
        Assert.Equal(4, modules.Select(m => m.Info.Name).Distinct().Count());
    }

    [Fact]
    public async Task Scan_ReturnsTheDecisionForItsDomain_WithTheLaunchPath()
    {
        var facts = new FakeHardwareFactsProvider(
            Desktop(CompanionObservation.Running(CompanionApp.FanControl)),
            new Dictionary<CompanionApp, string> { [CompanionApp.FanControl] = @"C:\FanControl\FanControl.exe" });
        var module = new CoolingModule(facts);

        var result = await module.ScanSystemStateAsync();

        var data = Assert.IsType<HardwareTabScanData>(result.Value);
        Assert.Equal(HardwareDomain.Cooling, data.Decision.Domain);
        Assert.Equal(HardwareAvailability.Available, data.Decision.Availability);
        Assert.Equal(CompanionActionKind.Open, data.Decision.Action?.Kind);
        Assert.Equal(@"C:\FanControl\FanControl.exe", data.LaunchPath);
        Assert.Contains("fake detection", data.DetectionNotes);
        Assert.Equal(MachineFormFactor.Desktop, data.Report.FormFactor.FormFactor);
    }

    [Fact]
    public async Task Scan_UsesTheCachedSnapshot_AndRefreshRunsANewPass()
    {
        var facts = new FakeHardwareFactsProvider(Desktop());
        var module = new LightingModule(facts);

        var first = await module.ScanSystemStateAsync();
        var second = await module.ScanSystemStateAsync();
        Assert.Equal(1, facts.Passes);
        // A fresh cache is not worth re-checking behind the page.
        Assert.False(Assert.IsType<HardwareTabScanData>(first.Value).RefreshInBackground);
        Assert.False(Assert.IsType<HardwareTabScanData>(second.Value).RefreshInBackground);

        var refreshed = await module.RefreshAsync();
        Assert.True(refreshed.IsSuccess);
        Assert.Equal(2, facts.Passes);
    }

    [Fact]
    public async Task Scan_OnAnOldCache_AsksThePageToRefreshBehindItself()
    {
        var facts = new FakeHardwareFactsProvider(Desktop(), observedAt: DateTimeOffset.Now - TimeSpan.FromMinutes(5));
        var module = new CoolingModule(facts);

        await module.ScanSystemStateAsync(); // first pass, nothing cached before it
        var reopened = await module.ScanSystemStateAsync();

        Assert.True(Assert.IsType<HardwareTabScanData>(reopened.Value).RefreshInBackground);
        Assert.Equal(1, facts.Passes);
    }

    [Fact]
    public async Task InvalidateSnapshot_MakesTheNextScanAFreshPass_WithNoBackgroundRefresh()
    {
        var facts = new FakeHardwareFactsProvider(Desktop(), observedAt: DateTimeOffset.Now - TimeSpan.FromMinutes(5));
        var module = new MonitoringModule(facts);
        await module.ScanSystemStateAsync();

        module.InvalidateSnapshot();
        var forced = await module.ScanSystemStateAsync();
        var after = await module.ScanSystemStateAsync();

        Assert.Equal(2, facts.Passes);
        Assert.Equal(1, facts.Refreshes);
        Assert.False(Assert.IsType<HardwareTabScanData>(forced.Value).RefreshInBackground);
        Assert.True(Assert.IsType<HardwareTabScanData>(after.Value).RefreshInBackground);
    }

    [Fact]
    public async Task Scan_OnADesktop_SystemControlIsUnavailableWithNoLaunchPath()
    {
        var facts = new FakeHardwareFactsProvider(Desktop());
        var module = new SystemControlModule(facts);

        var result = await module.ScanSystemStateAsync();

        var data = Assert.IsType<HardwareTabScanData>(result.Value);
        Assert.Equal(HardwareAvailability.Unavailable, data.Decision.Availability);
        Assert.Null(data.Decision.Action);
        Assert.Null(data.LaunchPath);
    }

    [Fact]
    public void Evaluate_ShowAllControlsSetting_ShowsControlsButGrantsNothing()
    {
        var settings = new FakeSettingsService();
        var facts = new FakeHardwareFactsProvider(Desktop());
        var module = new LightingModule(facts, settings);
        var snapshot = facts.GetAsync().Result;

        var before = module.Evaluate(snapshot);
        settings.SetApp(AppSettingKeys.ShowAllHardwareControls, "1");
        var after = module.Evaluate(snapshot);

        Assert.False(before.Decision.ControlsVisible);
        Assert.True(after.Decision.ControlsVisible);
        Assert.True(after.Report.VisibilityOverrideActive);
        Assert.Equal(before.Decision.Availability, after.Decision.Availability);
        Assert.Equal(before.Decision.Operations, after.Decision.Operations);
        Assert.False(after.Decision.LiveWritesAllowed);
    }

    [Fact]
    public async Task Scan_WhenDetectionThrows_ReportsAFailureInsteadOfCrashing()
    {
        var facts = new FakeHardwareFactsProvider(() => throw new InvalidOperationException("boom"));
        var module = new MonitoringModule(facts);

        var result = await module.ScanSystemStateAsync();

        Assert.False(result.IsSuccess);
        Assert.Contains("boom", result.ErrorMessage, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ApplyAndRevert_AreRefused_TheseTabsStageNothing()
    {
        var module = new CoolingModule(new FakeHardwareFactsProvider(Desktop()));
        var change = new ChangeDescriptor
        {
            ModuleId = CoolingModule.ModuleName,
            SettingId = "x",
            DisplayName = "x",
            SystemLocation = "x",
            BeforeValue = "a",
            AfterValue = "b",
            BeforeDisplay = "a",
            AfterDisplay = "b",
            ValueType = ChangeValueType.Registry_String,
        };

        Assert.False((await module.ApplyChangeAsync(change)).IsSuccess);
        Assert.False((await module.RevertChangeAsync(change)).IsSuccess);
    }
}
