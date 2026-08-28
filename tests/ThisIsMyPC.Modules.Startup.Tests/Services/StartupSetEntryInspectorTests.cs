using ThisIsMyPC.Core.Changes;
using ThisIsMyPC.Core.Services;
using ThisIsMyPC.Core.Sets;
using ThisIsMyPC.Modules.Startup.Changes;
using ThisIsMyPC.Modules.Startup.Services;
using ThisIsMyPC.Modules.Startup.Tests.Fakes;

namespace ThisIsMyPC.Modules.Startup.Tests.Services;

public sealed class StartupSetEntryInspectorTests
{
    private readonly FakeServiceControlService _services = new();
    private readonly FakeScheduledTaskService _tasks = new();
    private readonly FakeRegistryService _registry = new();
    private readonly FakeStartupFolderService _folders = new();

    private StartupSetEntryInspector Inspector => new(_services, _tasks, _registry, _folders);

    private static SetEntry Entry(string settingId, string value) => new()
    {
        ModuleId = "Startup & Services",
        SettingId = settingId,
        Value = value,
        Description = "d",
    };

    [Fact]
    public void UnknownSettingIdFormat_ReturnsNull()
    {
        Assert.Null(Inspector.Inspect(Entry("no-such-setting", "0")));
        Assert.Null(Inspector.CreateChangeGroup(Entry("no-such-setting", "0")));
    }

    [Fact]
    public void Service_AbsentOnThisMachine_ReturnsNull_WillBeSkipped()
    {
        Assert.Null(Inspector.Inspect(Entry("service-starttype:dptftcs", "Disabled")));
        Assert.Null(Inspector.CreateChangeGroup(Entry("service-starttype:dptftcs", "Disabled")));
    }

    [Fact]
    public void Service_ManualState_NotApplied_ForDisabledEntry()
    {
        _services.AddService("DiagTrack", startType: ServiceStartType.Manual, displayName: "Connected User Experiences and Telemetry");

        var state = Inspector.Inspect(Entry("service-starttype:DiagTrack", "Disabled"));

        Assert.NotNull(state);
        Assert.Equal("Service startup type: Connected User Experiences and Telemetry", state!.SettingDisplayName);
        Assert.Equal("Manual", state.CurrentValue);
        Assert.Equal("Manual", state.CurrentDisplay);
        Assert.False(state.IsApplied);
    }

    [Fact]
    public void Service_AlreadyDisabled_Applied()
    {
        _services.AddService("DiagTrack", startType: ServiceStartType.Disabled);

        var state = Inspector.Inspect(Entry("service-starttype:DiagTrack", "Disabled"));

        Assert.True(state!.IsApplied);
    }

    [Fact]
    public void Service_CreateChangeGroup_MirrorsTheModuleFactory()
    {
        _services.AddService("DiagTrack", startType: ServiceStartType.Automatic, displayName: "Telemetry");

        var group = Inspector.CreateChangeGroup(Entry("service-starttype:DiagTrack", "Disabled"));

        Assert.NotNull(group);
        var change = Assert.Single(group!.Changes);
        Assert.Equal("service-starttype:DiagTrack", change.SettingId);
        Assert.Equal(ChangeValueType.Service_StartType, change.ValueType);
        Assert.Equal("Automatic", change.BeforeValue);
        Assert.Equal("Disabled", change.AfterValue);
    }

    [Fact]
    public void Service_BogusValue_Unstageable()
    {
        _services.AddService("DiagTrack");

        Assert.Null(Inspector.CreateChangeGroup(Entry("service-starttype:DiagTrack", "4")));
        Assert.Null(Inspector.CreateChangeGroup(Entry("service-starttype:DiagTrack", "disabled")));
    }

    [Fact]
    public void Task_EnabledState_NotApplied_ForDisabledEntry()
    {
        _tasks.AddTask(@"\Microsoft\Windows\Autochk\Proxy", enabled: true);

        var state = Inspector.Inspect(Entry(@"scheduled-task:\Microsoft\Windows\Autochk\Proxy", "Disabled"));

        Assert.NotNull(state);
        Assert.Equal("Scheduled task: Proxy", state!.SettingDisplayName);
        Assert.Equal("Enabled", state.CurrentValue);
        Assert.False(state.IsApplied);
    }

    [Fact]
    public void Task_AbsentOnThisMachine_ReturnsNull()
    {
        Assert.Null(Inspector.Inspect(Entry(@"scheduled-task:\No\Such\Task", "Disabled")));
        Assert.Null(Inspector.CreateChangeGroup(Entry(@"scheduled-task:\No\Such\Task", "Disabled")));
    }

    [Fact]
    public void Task_CreateChangeGroup_MirrorsTheModuleFactory()
    {
        _tasks.AddTask(@"\Microsoft\Windows\Autochk\Proxy", enabled: true);

        var group = Inspector.CreateChangeGroup(Entry(@"scheduled-task:\Microsoft\Windows\Autochk\Proxy", "Disabled"));

        Assert.NotNull(group);
        var change = Assert.Single(group!.Changes);
        Assert.Equal(@"scheduled-task:\Microsoft\Windows\Autochk\Proxy", change.SettingId);
        Assert.Equal(ChangeValueType.ScheduledTask_State, change.ValueType);
        Assert.Equal("Enabled", change.BeforeValue);
        Assert.Equal("Disabled", change.AfterValue);
    }

    [Fact]
    public void Task_BogusValue_Unstageable()
    {
        _tasks.AddTask(@"\Microsoft\Windows\Autochk\Proxy");

        Assert.Null(Inspector.CreateChangeGroup(Entry(@"scheduled-task:\Microsoft\Windows\Autochk\Proxy", "off")));
    }

    [Fact]
    public void StartupEntry_EnabledRunValue_NotApplied_ForDisableEntry()
    {
        _registry.SetString(StartupScanner.UserRunKey, "OneDrive", @"C:\OneDrive.exe /background");

        var state = Inspector.Inspect(Entry(
            "startup-entry:RegistryUserRun:OneDrive",
            Convert.ToHexString(StartupChangeFactory.DisabledBlob)));

        Assert.NotNull(state);
        Assert.Equal("Startup entry: OneDrive", state!.SettingDisplayName);
        Assert.Equal("Enabled", state.CurrentDisplay);
        Assert.False(state.IsApplied);
    }

    [Fact]
    public void StartupEntry_DisabledBlob_Applied_ForDisableEntry()
    {
        _registry.SetString(StartupScanner.UserRunKey, "OneDrive", @"C:\OneDrive.exe");
        _registry.SetBinary(StartupScanner.UserApprovedRunKey, "OneDrive", [0x03, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0]);

        var state = Inspector.Inspect(Entry(
            "startup-entry:RegistryUserRun:OneDrive",
            Convert.ToHexString(StartupChangeFactory.DisabledBlob)));

        Assert.Equal("Disabled", state!.CurrentDisplay);
        Assert.True(state.IsApplied);
    }

    [Fact]
    public void StartupEntry_RunValueGone_ReturnsNull_NoOrphanApprovedWrite()
    {
        // Only the leftover StartupApproved blob exists — the actual Run value is gone.
        _registry.SetBinary(StartupScanner.UserApprovedRunKey, "Ghost", [0x02, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0]);

        var value = Convert.ToHexString(StartupChangeFactory.DisabledBlob);
        Assert.Null(Inspector.Inspect(Entry("startup-entry:RegistryUserRun:Ghost", value)));
        Assert.Null(Inspector.CreateChangeGroup(Entry("startup-entry:RegistryUserRun:Ghost", value)));
    }

    [Fact]
    public void StartupEntry_CreateChangeGroup_MirrorsTheModuleFactory()
    {
        _registry.SetString(StartupScanner.UserRunKey, "OneDrive", @"C:\OneDrive.exe");

        var group = Inspector.CreateChangeGroup(Entry(
            "startup-entry:RegistryUserRun:OneDrive",
            Convert.ToHexString(StartupChangeFactory.DisabledBlob)));

        Assert.NotNull(group);
        var change = Assert.Single(group!.Changes);
        Assert.Equal("startup-entry:RegistryUserRun:OneDrive", change.SettingId);
        Assert.Equal(ChangeValueType.Registry_Binary, change.ValueType);
        Assert.Equal(string.Empty, change.BeforeValue);
        Assert.Equal(Convert.ToHexString(StartupChangeFactory.DisabledBlob), change.AfterValue);
    }

    [Fact]
    public void StartupEntry_BogusValueOrSource_Unstageable()
    {
        _registry.SetString(StartupScanner.UserRunKey, "OneDrive", @"C:\OneDrive.exe");

        Assert.Null(Inspector.CreateChangeGroup(Entry("startup-entry:RegistryUserRun:OneDrive", "Disabled")));
        Assert.Null(Inspector.CreateChangeGroup(Entry(
            "startup-entry:NoSuchSource:OneDrive", Convert.ToHexString(StartupChangeFactory.DisabledBlob))));
    }
}
