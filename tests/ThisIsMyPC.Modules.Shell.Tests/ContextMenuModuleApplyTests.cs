using ThisIsMyPC.Core.Changes;
using ThisIsMyPC.Core.Results;
using ThisIsMyPC.Interop.Com.Shell;
using ThisIsMyPC.Modules.Shell.Tests.Fakes;

namespace ThisIsMyPC.Modules.Shell.Tests;

public sealed class ContextMenuModuleApplyTests
{
    private readonly FakeRegistryService _registry = new();
    private readonly ContextMenuModule _module;

    public ContextMenuModuleApplyTests()
    {
        var shellExtSvc = new ShellExtensionService(_registry);
        _module = new ContextMenuModule(_registry, shellExtSvc, new NullContextMenuProbe());
    }

    [Fact]
    public async Task ApplyBlockedListDisable_writes_empty_string_value()
    {
        var blockedPath = @"HKLM\SOFTWARE\Microsoft\Windows\CurrentVersion\Shell Extensions\Blocked";
        _registry.AddKey(blockedPath);

        var change = new ChangeDescriptor
        {
            ModuleId = "Context Menus",
            SettingId = "ctx-handler-12345678-1234-1234-1234-123456789ABC",
            DisplayName = "Context menu: TestHandler",
            SystemLocation = $@"{blockedPath}\{{12345678-1234-1234-1234-123456789ABC}}",
            BeforeValue = "__absent__",
            AfterValue = "",
            BeforeDisplay = "Enabled",
            AfterDisplay = "Disabled",
            ValueType = ChangeValueType.Registry_String,
            Category = ChangeCategory.Disable,
        };

        var result = await _module.ApplyChangeAsync(change);

        Assert.True(result.IsSuccess);
        var readResult = _registry.ReadString(blockedPath, "{12345678-1234-1234-1234-123456789ABC}");
        Assert.True(readResult.IsSuccess);
        Assert.Equal("", readResult.Value);
    }

    [Fact]
    public async Task ApplyBlockedListEnable_deletes_value()
    {
        var blockedPath = @"HKLM\SOFTWARE\Microsoft\Windows\CurrentVersion\Shell Extensions\Blocked";
        _registry.AddKey(blockedPath);
        _registry.SetString(blockedPath, "{12345678-1234-1234-1234-123456789ABC}", "");

        var change = new ChangeDescriptor
        {
            ModuleId = "Context Menus",
            SettingId = "ctx-handler-12345678-1234-1234-1234-123456789ABC",
            DisplayName = "Context menu: TestHandler",
            SystemLocation = $@"{blockedPath}\{{12345678-1234-1234-1234-123456789ABC}}",
            BeforeValue = "",
            AfterValue = "__absent__",
            BeforeDisplay = "Disabled",
            AfterDisplay = "Enabled",
            ValueType = ChangeValueType.Registry_String,
            Category = ChangeCategory.Enable,
        };

        var result = await _module.ApplyChangeAsync(change);

        Assert.True(result.IsSuccess);
        var existsResult = _registry.ValueExists(blockedPath, "{12345678-1234-1234-1234-123456789ABC}");
        Assert.False(existsResult.Value);
    }

    [Fact]
    public async Task ApplyDashPrefixToggle_still_works()
    {
        // Existing dash-prefix mechanism should continue to work
        var handlerPath = @"HKCR\*\shellex\ContextMenuHandlers\TestHandler";
        _registry.AddKey(handlerPath);
        _registry.SetString(handlerPath, string.Empty, "{12345678-1234-1234-1234-123456789ABC}");

        var change = new ChangeDescriptor
        {
            ModuleId = "Context Menus",
            SettingId = "ctx-handler-12345678-1234-1234-1234-123456789ABC",
            DisplayName = "Context menu: TestHandler",
            SystemLocation = $@"{handlerPath}\(Default)",
            BeforeValue = "{12345678-1234-1234-1234-123456789ABC}",
            AfterValue = "-{12345678-1234-1234-1234-123456789ABC}",
            BeforeDisplay = "Enabled",
            AfterDisplay = "Disabled",
            ValueType = ChangeValueType.Registry_String,
            Category = ChangeCategory.Disable,
        };

        var result = await _module.ApplyChangeAsync(change);

        Assert.True(result.IsSuccess);
        var readResult = _registry.ReadString(handlerPath, string.Empty);
        Assert.Equal("-{12345678-1234-1234-1234-123456789ABC}", readResult.Value);
    }

    [Fact]
    public async Task RevertBlockedListDisable_deletes_value()
    {
        // After a disable (AfterValue="", BeforeValue="__absent__"),
        // revert should restore BeforeValue="__absent__" → delete the value
        var blockedPath = @"HKLM\SOFTWARE\Microsoft\Windows\CurrentVersion\Shell Extensions\Blocked";
        _registry.AddKey(blockedPath);
        _registry.SetString(blockedPath, "{12345678-1234-1234-1234-123456789ABC}", "");

        var change = new ChangeDescriptor
        {
            ModuleId = "Context Menus",
            SettingId = "ctx-handler-12345678-1234-1234-1234-123456789ABC",
            DisplayName = "Context menu: TestHandler",
            SystemLocation = $@"{blockedPath}\{{12345678-1234-1234-1234-123456789ABC}}",
            BeforeValue = "__absent__",
            AfterValue = "",
            BeforeDisplay = "Enabled",
            AfterDisplay = "Disabled",
            ValueType = ChangeValueType.Registry_String,
            Category = ChangeCategory.Disable,
        };

        var result = await _module.RevertChangeAsync(change);

        Assert.True(result.IsSuccess);
        var existsResult = _registry.ValueExists(blockedPath, "{12345678-1234-1234-1234-123456789ABC}");
        Assert.False(existsResult.Value);
    }

    [Fact]
    public async Task RevertBlockedListEnable_restores_empty_string_value()
    {
        // After an enable (AfterValue="__absent__", BeforeValue=""),
        // revert should restore BeforeValue="" → write empty string back
        var blockedPath = @"HKLM\SOFTWARE\Microsoft\Windows\CurrentVersion\Shell Extensions\Blocked";
        _registry.AddKey(blockedPath);

        var change = new ChangeDescriptor
        {
            ModuleId = "Context Menus",
            SettingId = "ctx-handler-12345678-1234-1234-1234-123456789ABC",
            DisplayName = "Context menu: TestHandler",
            SystemLocation = $@"{blockedPath}\{{12345678-1234-1234-1234-123456789ABC}}",
            BeforeValue = "",
            AfterValue = "__absent__",
            BeforeDisplay = "Disabled",
            AfterDisplay = "Enabled",
            ValueType = ChangeValueType.Registry_String,
            Category = ChangeCategory.Enable,
        };

        var result = await _module.RevertChangeAsync(change);

        Assert.True(result.IsSuccess);
        var readResult = _registry.ReadString(blockedPath, "{12345678-1234-1234-1234-123456789ABC}");
        Assert.True(readResult.IsSuccess);
        Assert.Equal("", readResult.Value);
    }

    [Fact]
    public async Task RevertDashPrefixDisable_restores_clean_clsid()
    {
        var handlerPath = @"HKCR\*\shellex\ContextMenuHandlers\TestHandler";
        _registry.AddKey(handlerPath);
        _registry.SetString(handlerPath, string.Empty, "-{12345678-1234-1234-1234-123456789ABC}");

        var change = new ChangeDescriptor
        {
            ModuleId = "Context Menus",
            SettingId = "ctx-handler-12345678-1234-1234-1234-123456789ABC",
            DisplayName = "Context menu: TestHandler",
            SystemLocation = $@"{handlerPath}\(Default)",
            BeforeValue = "{12345678-1234-1234-1234-123456789ABC}",
            AfterValue = "-{12345678-1234-1234-1234-123456789ABC}",
            BeforeDisplay = "Enabled",
            AfterDisplay = "Disabled",
            ValueType = ChangeValueType.Registry_String,
            Category = ChangeCategory.Disable,
        };

        var result = await _module.RevertChangeAsync(change);

        Assert.True(result.IsSuccess);
        var readResult = _registry.ReadString(handlerPath, string.Empty);
        Assert.Equal("{12345678-1234-1234-1234-123456789ABC}", readResult.Value);
    }

    private sealed class NullContextMenuProbe : IContextMenuProbe
    {
        public OperationResult<bool> HandlerAppearsOnSurface(string clsid, ContextMenuSurface surface)
            => OperationResult<bool>.Success(true);
    }
}
