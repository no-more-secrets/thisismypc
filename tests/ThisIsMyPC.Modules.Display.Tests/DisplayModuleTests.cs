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
