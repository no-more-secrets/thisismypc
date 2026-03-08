using ThisIsMyPC.Core.Changes;
using ThisIsMyPC.Modules.Shell.Tests.Fakes;

namespace ThisIsMyPC.Modules.Shell.Tests;

public sealed class ShellModuleApplyTests
{
    private readonly FakeRegistryService _registry = new();
    private readonly ShellModule _module;

    public ShellModuleApplyTests()
    {
        _module = new ShellModule(_registry);
    }

    [Fact]
    public async Task ApplyDWordChange_writes_correct_value()
    {
        var change = new ChangeDescriptor
        {
            ModuleId = "Explorer",
            SettingId = "taskbar-alignment",
            DisplayName = "Taskbar alignment",
            SystemLocation = @"HKCU\Software\Microsoft\Windows\CurrentVersion\Explorer\Advanced\TaskbarAl",
            BeforeValue = "1",
            AfterValue = "0",
            BeforeDisplay = "Center",
            AfterDisplay = "Left",
            ValueType = ChangeValueType.Registry_DWord,
            Category = ChangeCategory.Modify,
        };

        var result = await _module.ApplyChangeAsync(change);

        Assert.True(result.IsSuccess);
        var readBack = _registry.ReadDWord(
            @"HKCU\Software\Microsoft\Windows\CurrentVersion\Explorer\Advanced", "TaskbarAl");
        Assert.Equal(0, readBack.Value);
    }

    [Fact]
    public async Task ApplyDWordChange_widgets_toggle()
    {
        var change = new ChangeDescriptor
        {
            ModuleId = "Explorer",
            SettingId = "taskbar-widgets",
            DisplayName = "Taskbar widgets",
            SystemLocation = @"HKCU\Software\Microsoft\Windows\CurrentVersion\Explorer\Advanced\TaskbarDa",
            BeforeValue = "1",
            AfterValue = "0",
            BeforeDisplay = "Shown",
            AfterDisplay = "Hidden",
            ValueType = ChangeValueType.Registry_DWord,
            Category = ChangeCategory.Disable,
        };

        var result = await _module.ApplyChangeAsync(change);

        Assert.True(result.IsSuccess);
        var readBack = _registry.ReadDWord(
            @"HKCU\Software\Microsoft\Windows\CurrentVersion\Explorer\Advanced", "TaskbarDa");
        Assert.Equal(0, readBack.Value);
    }

    [Fact]
    public async Task ApplyClassicContextMenuChange_enable_creates_key()
    {
        var change = new ChangeDescriptor
        {
            ModuleId = "Explorer",
            SettingId = "classic-context-menu",
            DisplayName = "Classic context menu",
            SystemLocation = ShellRegistryPaths.ClassicContextMenuKeyPath,
            BeforeValue = ShellRegistryPaths.AbsentValue,
            AfterValue = "",
            BeforeDisplay = "Disabled",
            AfterDisplay = "Enabled",
            ValueType = ChangeValueType.Registry_String,
            Category = ChangeCategory.Enable,
            RestartRequirement = RestartRequirement.None,
        };

        var result = await _module.ApplyChangeAsync(change);

        Assert.True(result.IsSuccess);
        var keyExists = _registry.KeyExists(ShellRegistryPaths.ClassicContextMenuKeyPath);
        Assert.True(keyExists.Value);
    }

    [Fact]
    public async Task ApplyClassicContextMenuChange_disable_deletes_key()
    {
        // First create the key
        _registry.AddKey(ShellRegistryPaths.ClassicContextMenuKeyPath);
        _registry.AddKey(ShellRegistryPaths.ClassicContextMenuClsidKeyPath);

        var change = new ChangeDescriptor
        {
            ModuleId = "Explorer",
            SettingId = "classic-context-menu",
            DisplayName = "Classic context menu",
            SystemLocation = ShellRegistryPaths.ClassicContextMenuKeyPath,
            BeforeValue = "",
            AfterValue = ShellRegistryPaths.AbsentValue,
            BeforeDisplay = "Enabled",
            AfterDisplay = "Disabled",
            ValueType = ChangeValueType.Registry_String,
            Category = ChangeCategory.Disable,
            RestartRequirement = RestartRequirement.None,
        };

        var result = await _module.ApplyChangeAsync(change);

        Assert.True(result.IsSuccess);
    }
}
