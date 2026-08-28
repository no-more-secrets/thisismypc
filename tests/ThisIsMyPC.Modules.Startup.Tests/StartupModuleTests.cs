using ThisIsMyPC.Core.Changes;
using ThisIsMyPC.Core.Services;
using ThisIsMyPC.Modules.Startup.Changes;
using ThisIsMyPC.Modules.Startup.Models;
using ThisIsMyPC.Modules.Startup.Services;
using ThisIsMyPC.Modules.Startup.Tests.Fakes;

namespace ThisIsMyPC.Modules.Startup.Tests;

public class StartupModuleTests
{
    private readonly FakeRegistryService _registry = new();
    private readonly FakeStartupFolderService _folders = new();
    private readonly FakeServiceControlService _services = new();
    private readonly FakeScheduledTaskService _tasks = new();

    private StartupModule CreateModule() => new(_registry, _folders, _services, _tasks,
        new TaskClassificationOverrideStore(Path.Combine(Path.GetTempPath(), $"tipc-mod-{Guid.NewGuid():N}.txt")));

    private static StartupEntry MakeEntry() => new()
    {
        Name = "App",
        Command = @"C:\app.exe",
        Source = StartupSource.RegistryUserRun,
        SourceLocation = StartupScanner.UserRunKey,
        IsEnabled = true,
    };

    [Fact]
    public async Task ApplyChange_WritesDisabledBlob()
    {
        var change = StartupChangeFactory.CreateToggle(MakeEntry(), enable: false, currentApprovedBlob: null)!;

        var result = await CreateModule().ApplyChangeAsync(change);

        Assert.True(result.IsSuccess);
        var written = _registry.ReadBinary(StartupScanner.UserApprovedRunKey, "App");
        Assert.True(written.IsSuccess);
        Assert.Equal(StartupChangeFactory.DisabledBlob, written.Value);
    }

    [Fact]
    public async Task RevertChange_AbsentBeforeValue_DeletesTheValue()
    {
        var change = StartupChangeFactory.CreateToggle(MakeEntry(), enable: false, currentApprovedBlob: null)!;
        var module = CreateModule();
        await module.ApplyChangeAsync(change);

        // Revert contract: Before/After swapped descriptor
        var reverted = change with { BeforeValue = change.AfterValue!, AfterValue = change.BeforeValue };
        var result = await module.RevertChangeAsync(reverted);

        Assert.True(result.IsSuccess);
        Assert.False(_registry.ValueExists(StartupScanner.UserApprovedRunKey, "App").Value);
    }

    [Fact]
    public async Task RevertChange_ExistingBeforeBlob_RestoresIt()
    {
        var original = new byte[] { 0x02, 0, 0, 0, 0xAA, 0xBB, 0, 0, 0, 0, 0, 0 };
        _registry.SetBinary(StartupScanner.UserApprovedRunKey, "App", original);
        var change = StartupChangeFactory.CreateToggle(MakeEntry(), enable: false, currentApprovedBlob: original)!;
        var module = CreateModule();
        await module.ApplyChangeAsync(change);

        var reverted = change with { BeforeValue = change.AfterValue!, AfterValue = change.BeforeValue };
        var result = await module.RevertChangeAsync(reverted);

        Assert.True(result.IsSuccess);
        Assert.Equal(original, _registry.ReadBinary(StartupScanner.UserApprovedRunKey, "App").Value);
    }

    [Fact]
    public async Task ApplyChange_UnsupportedValueType_Fails()
    {
        var change = StartupChangeFactory.CreateToggle(MakeEntry(), enable: false, currentApprovedBlob: null)!
            with { ValueType = ChangeValueType.PowerPlan_Setting };

        var result = await CreateModule().ApplyChangeAsync(change);

        Assert.False(result.IsSuccess);
        Assert.Contains("Unsupported", result.ErrorMessage, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ApplyChange_MalformedHex_Fails()
    {
        var change = StartupChangeFactory.CreateToggle(MakeEntry(), enable: false, currentApprovedBlob: null)!
            with { AfterValue = "not-hex" };

        var result = await CreateModule().ApplyChangeAsync(change);

        Assert.False(result.IsSuccess);
    }

    [Fact]
    public async Task ScanSystemState_ReturnsStartupScanData()
    {
        _registry.SetString(StartupScanner.UserRunKey, "App", @"C:\app.exe");
        _services.AddService("Spooler", ServiceState.Running, ServiceStartType.Automatic);
        _tasks.AddTask(@"\Vendor\DailyThing", triggers: ["CalendarTrigger"]);

        var result = await CreateModule().ScanSystemStateAsync();

        Assert.True(result.IsSuccess);
        var data = Assert.IsType<StartupScanData>(result.Value);
        Assert.Single(data.StartupEntries);
        Assert.Single(data.Services);
        Assert.Single(data.ScheduledTasks);
    }

    [Fact]
    public async Task ScanSystemState_LogonTasks_SurfaceAsStartupEntries()
    {
        _tasks.AddTask(@"\Vendor\LogonThing", triggers: ["LogonTrigger"]);
        _tasks.AddTask(@"\Vendor\DailyThing", triggers: ["CalendarTrigger"]);

        var result = await CreateModule().ScanSystemStateAsync();

        var data = Assert.IsType<StartupScanData>(result.Value);
        var startupEntry = Assert.Single(data.StartupEntries);
        Assert.Equal(StartupSource.ScheduledTask, startupEntry.Source);
        Assert.Equal(@"\Vendor\LogonThing", startupEntry.SourceLocation);
        Assert.Equal(2, data.ScheduledTasks.Count); // full list still has both
    }

    [Fact]
    public async Task ApplyChange_ScheduledTaskState_CallsSetEnabled()
    {
        _tasks.AddTask(@"\Vendor\Thing", enabled: true);
        var entry = new ScheduledTaskEntry
        {
            Name = "Thing",
            Path = @"\Vendor\Thing",
            IsEnabled = true,
            Classification = TaskClassification.Unknown,
        };
        var change = ScheduledTaskChangeFactory.CreateToggle(entry, enable: false);

        var result = await CreateModule().ApplyChangeAsync(change);

        Assert.True(result.IsSuccess);
        Assert.Contains(@"SetEnabled:\Vendor\Thing:False", _tasks.Calls);
        Assert.False(_tasks.GetTask(@"\Vendor\Thing")!.IsEnabled);
    }

    [Fact]
    public async Task RevertChange_ScheduledTaskState_RestoresEnabled()
    {
        _tasks.AddTask(@"\Vendor\Thing", enabled: true);
        var entry = new ScheduledTaskEntry
        {
            Name = "Thing",
            Path = @"\Vendor\Thing",
            IsEnabled = true,
            Classification = TaskClassification.Unknown,
        };
        var change = ScheduledTaskChangeFactory.CreateToggle(entry, enable: false);
        var module = CreateModule();
        await module.ApplyChangeAsync(change);

        var reverted = change with { BeforeValue = change.AfterValue!, AfterValue = change.BeforeValue };
        var result = await module.RevertChangeAsync(reverted);

        Assert.True(result.IsSuccess);
        Assert.True(_tasks.GetTask(@"\Vendor\Thing")!.IsEnabled);
    }

    [Fact]
    public async Task ApplyChange_ServiceStartType_CallsSetStartType()
    {
        _services.AddService("Spooler", ServiceState.Running, ServiceStartType.Automatic);
        var entry = new ServiceEntry
        {
            ServiceName = "Spooler",
            DisplayName = "Print Spooler",
            State = ServiceState.Running,
            StartType = ServiceStartType.Automatic,
        };
        var change = ServiceChangeFactory.CreateStartTypeChange(entry, ServiceStartType.Disabled);

        var result = await CreateModule().ApplyChangeAsync(change);

        Assert.True(result.IsSuccess);
        Assert.Contains("SetStartType:Spooler:Disabled", _services.Calls);
        Assert.Equal(ServiceStartType.Disabled, _services.GetService("Spooler")!.StartType);
    }

    [Fact]
    public async Task RevertChange_ServiceStartType_RestoresBeforeValue()
    {
        _services.AddService("Spooler", ServiceState.Running, ServiceStartType.Automatic);
        var entry = new ServiceEntry
        {
            ServiceName = "Spooler",
            DisplayName = "Print Spooler",
            State = ServiceState.Running,
            StartType = ServiceStartType.Automatic,
        };
        var change = ServiceChangeFactory.CreateStartTypeChange(entry, ServiceStartType.Disabled);
        var module = CreateModule();
        await module.ApplyChangeAsync(change);

        var reverted = change with { BeforeValue = change.AfterValue!, AfterValue = change.BeforeValue };
        var result = await module.RevertChangeAsync(reverted);

        Assert.True(result.IsSuccess);
        Assert.Equal(ServiceStartType.Automatic, _services.GetService("Spooler")!.StartType);
    }

    [Fact]
    public async Task ApplyChange_ServiceStartType_InvalidEnum_Fails()
    {
        var entry = new ServiceEntry
        {
            ServiceName = "Spooler",
            DisplayName = "Print Spooler",
            State = ServiceState.Running,
            StartType = ServiceStartType.Automatic,
        };
        var change = ServiceChangeFactory.CreateStartTypeChange(entry, ServiceStartType.Disabled)
            with { AfterValue = "NotAStartType" };

        var result = await CreateModule().ApplyChangeAsync(change);

        Assert.False(result.IsSuccess);
        Assert.Empty(_services.Calls);
    }
}
