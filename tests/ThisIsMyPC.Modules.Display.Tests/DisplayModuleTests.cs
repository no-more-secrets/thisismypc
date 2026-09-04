using ThisIsMyPC.Core.Changes;
using ThisIsMyPC.Core.Display;
using ThisIsMyPC.Core.Services;
using ThisIsMyPC.Modules.Display.Models;
using ThisIsMyPC.Modules.Display.Services;
using ThisIsMyPC.Modules.Display.Tests.Fakes;

namespace ThisIsMyPC.Modules.Display.Tests;

public sealed class DisplayModuleTests
{
    private static MonitorDevice External(string id = @"\\.\DISPLAY1|0") => new()
    {
        Id = id,
        Name = "VG27AQ",
        SupportsDdc = true,
        Brightness = 50,
    };

    private static FakePowerService PowerWithPanel()
    {
        var power = new FakePowerService();
        var plan = Guid.NewGuid();
        power.AddPlan(plan, "Balanced", isActive: true);
        power.AddSetting(new PowerSettingInfo(
            InternalPanelService.VideoSubgroup, "Display",
            InternalPanelService.BrightnessSetting, "Display brightness",
            null, 70, 40, "%", IsRange: true, Min: 0, Max: 100, Increment: 1, PossibleValues: []));
        return power;
    }

    [Fact]
    public async Task Scan_OnALaptop_PutsTheInternalPanelFirst()
    {
        var monitors = new FakeMonitorService { HasBattery = true };
        monitors.Devices.Add(External());

        var result = await new DisplayModule(monitors, PowerWithPanel()).ScanSystemStateAsync();

        var data = Assert.IsType<DisplayScanData>(result.Value);
        Assert.Equal(2, data.Monitors.Count);
        Assert.True(data.Monitors[0].IsInternalPanel);
        Assert.Equal("VG27AQ", data.Monitors[1].Name);
        Assert.Null(data.ScanError);
    }

    [Fact]
    public async Task FirstScan_IsQuick_AndMarksFeaturesPending()
    {
        var monitors = new FakeMonitorService();
        monitors.Devices.Add(External());
        var module = new DisplayModule(monitors, PowerWithPanel());

        var data = Assert.IsType<DisplayScanData>((await module.ScanSystemStateAsync()).Value);

        Assert.Equal(["EnumerateMonitors:Quick"], monitors.Calls);
        Assert.True(data.IsPartial);
        Assert.True(data.Monitors[0].FeaturesPending);
        Assert.Same(data, module.Snapshot);
    }

    [Fact]
    public async Task SecondScan_ReturnsTheSnapshot_WithoutTouchingTheBus()
    {
        var monitors = new FakeMonitorService();
        monitors.Devices.Add(External());
        var module = new DisplayModule(monitors, PowerWithPanel());
        var first = (await module.ScanSystemStateAsync()).Value;

        var second = (await module.ScanSystemStateAsync()).Value;

        Assert.Same(first, second);
        Assert.Single(monitors.Calls);
    }

    [Fact]
    public async Task Refresh_RunsTheFullScan_AndBecomesTheSnapshot()
    {
        var monitors = new FakeMonitorService();
        monitors.Devices.Add(External() with { VendorFeatures = [new VendorVcpFeature(0xE6, "Blue light filter", [0, 1, 2], 1, true)] });
        var module = new DisplayModule(monitors, PowerWithPanel());
        await module.ScanSystemStateAsync();

        var full = await module.RefreshAsync();

        Assert.True(full.IsSuccess);
        Assert.False(full.Value!.IsPartial);
        Assert.Single(full.Value.Monitors[0].VendorFeatures);
        Assert.Same(full.Value, module.Snapshot);
        Assert.Equal(["EnumerateMonitors:Quick", "EnumerateMonitors:Full"], monitors.Calls);

        // A later quick scan never downgrades a full snapshot.
        var again = (await module.ScanSystemStateAsync()).Value;
        Assert.Same(full.Value, again);
    }

    [Fact]
    public async Task Invalidate_MakesTheNextOpenScanAgain()
    {
        var monitors = new FakeMonitorService();
        monitors.Devices.Add(External());
        var module = new DisplayModule(monitors, PowerWithPanel());
        await module.RefreshAsync();

        module.InvalidateSnapshot();
        Assert.Null(module.Snapshot);
        await module.ScanSystemStateAsync();

        Assert.Equal(["EnumerateMonitors:Full", "EnumerateMonitors:Quick"], monitors.Calls);
    }

    [Fact]
    public async Task Scan_WithoutABattery_ListsOnlyDdcMonitors()
    {
        var monitors = new FakeMonitorService { HasBattery = false };
        monitors.Devices.Add(External());

        var result = await new DisplayModule(monitors, PowerWithPanel()).ScanSystemStateAsync();

        var data = Assert.IsType<DisplayScanData>(result.Value);
        Assert.Single(data.Monitors);
        Assert.False(data.Monitors[0].IsInternalPanel);
    }

    [Fact]
    public async Task Scan_DdcFailureSurfacesButThePanelSurvives()
    {
        var monitors = new FakeMonitorService
        {
            HasBattery = true,
            EnumerateFailure = "DDC exploded",
        };

        var result = await new DisplayModule(monitors, PowerWithPanel()).ScanSystemStateAsync();

        var data = Assert.IsType<DisplayScanData>(result.Value);
        Assert.Single(data.Monitors);
        Assert.True(data.Monitors[0].IsInternalPanel);
        Assert.Equal("DDC exploded", data.ScanError);
    }

    [Fact]
    public async Task ApplyChange_IsRefused()
    {
        var module = new DisplayModule(new FakeMonitorService(), new FakePowerService());

        var result = await module.ApplyChangeAsync(new ChangeDescriptor
        {
            ModuleId = "Display",
            SettingId = "anything",
            DisplayName = "n/a",
            SystemLocation = "n/a",
            BeforeValue = "0",
            AfterValue = "1",
            BeforeDisplay = "0",
            AfterDisplay = "1",
            ValueType = ChangeValueType.Registry_DWord,
            Category = ChangeCategory.Modify,
        });

        Assert.False(result.IsSuccess);
    }
}
