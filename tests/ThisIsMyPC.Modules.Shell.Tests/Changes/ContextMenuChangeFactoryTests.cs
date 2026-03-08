using ThisIsMyPC.Core.Changes;
using ThisIsMyPC.Modules.Shell.Changes;
using ThisIsMyPC.Modules.Shell.Models;

namespace ThisIsMyPC.Modules.Shell.Tests.Changes;

public sealed class ContextMenuChangeFactoryTests
{
    private static ContextMenuHandler MakeHandler(bool isEnabled = true) =>
        new(
            Name: "TestHandler",
            Clsid: "{12345678-1234-1234-1234-123456789ABC}",
            RegistryPath: @"HKCR\*\shellex\ContextMenuHandlers\TestHandler",
            AppliesTo: "All files",
            DllPath: @"C:\Windows\System32\test.dll",
            Publisher: "TestPublisher",
            IsEnabled: isEnabled);

    private static ContextMenuHandler MakeMultiPathHandler(bool isEnabled = true) =>
        new(
            Name: "7-Zip Shell Extension",
            Clsid: "{23170F69-40C1-278A-1000-000100020000}",
            RegistryPath: @"HKCR\*\shellex\ContextMenuHandlers\7-Zip",
            AppliesTo: "All files",
            DllPath: @"C:\Program Files\7-Zip\7-zip.dll",
            Publisher: "Igor Pavlov",
            IsEnabled: isEnabled,
            AllRegistryPaths:
            [
                @"HKCR\*\shellex\ContextMenuHandlers\7-Zip",
                @"HKCR\Directory\shellex\ContextMenuHandlers\7-Zip",
                @"HKCR\Folder\shellex\ContextMenuHandlers\7-Zip",
            ],
            AllScopes: ["All files", "Directories", "Folders"]);

    [Fact]
    public void CreateToggle_enable_removes_dash_prefix()
    {
        var handler = MakeHandler(isEnabled: false);
        var changes = ContextMenuChangeFactory.CreateToggle(handler, enable: true);

        Assert.Single(changes);
        Assert.Equal("{12345678-1234-1234-1234-123456789ABC}", changes[0].AfterValue);
        Assert.Equal("-{12345678-1234-1234-1234-123456789ABC}", changes[0].BeforeValue);
        Assert.Equal(ChangeCategory.Enable, changes[0].Category);
    }

    [Fact]
    public void CreateToggle_disable_adds_dash_prefix()
    {
        var handler = MakeHandler(isEnabled: true);
        var changes = ContextMenuChangeFactory.CreateToggle(handler, enable: false);

        Assert.Single(changes);
        Assert.Equal("-{12345678-1234-1234-1234-123456789ABC}", changes[0].AfterValue);
        Assert.Equal("{12345678-1234-1234-1234-123456789ABC}", changes[0].BeforeValue);
        Assert.Equal(ChangeCategory.Disable, changes[0].Category);
    }

    [Fact]
    public void CreateToggle_sets_correct_system_location()
    {
        var handler = MakeHandler();
        var changes = ContextMenuChangeFactory.CreateToggle(handler, enable: true);

        Assert.Equal(@"HKCR\*\shellex\ContextMenuHandlers\TestHandler\(Default)", changes[0].SystemLocation);
    }

    [Fact]
    public void CreateToggle_uses_Registry_String_value_type()
    {
        var handler = MakeHandler();
        var changes = ContextMenuChangeFactory.CreateToggle(handler, enable: true);

        Assert.Equal(ChangeValueType.Registry_String, changes[0].ValueType);
    }

    [Fact]
    public void CreateToggle_sets_module_id()
    {
        var handler = MakeHandler();
        var changes = ContextMenuChangeFactory.CreateToggle(handler, enable: true);

        Assert.Equal("Context Menus", changes[0].ModuleId);
    }

    [Fact]
    public void CreateToggle_uses_CLSID_based_setting_id()
    {
        var handler = MakeHandler();
        var changes = ContextMenuChangeFactory.CreateToggle(handler, enable: true);

        Assert.Equal("ctx-handler-12345678-1234-1234-1234-123456789ABC", changes[0].SettingId);
    }

    [Fact]
    public void CreateToggle_multi_path_produces_one_descriptor_per_path()
    {
        var handler = MakeMultiPathHandler();
        var changes = ContextMenuChangeFactory.CreateToggle(handler, enable: false);

        Assert.Equal(3, changes.Count);
    }

    [Fact]
    public void CreateToggle_multi_path_all_share_same_setting_id()
    {
        var handler = MakeMultiPathHandler();
        var changes = ContextMenuChangeFactory.CreateToggle(handler, enable: false);

        var settingId = changes[0].SettingId;
        Assert.All(changes, c => Assert.Equal(settingId, c.SettingId));
        Assert.Equal("ctx-handler-23170F69-40C1-278A-1000-000100020000", settingId);
    }

    [Fact]
    public void CreateToggle_multi_path_each_has_distinct_system_location()
    {
        var handler = MakeMultiPathHandler();
        var changes = ContextMenuChangeFactory.CreateToggle(handler, enable: false);

        Assert.Equal(@"HKCR\*\shellex\ContextMenuHandlers\7-Zip\(Default)", changes[0].SystemLocation);
        Assert.Equal(@"HKCR\Directory\shellex\ContextMenuHandlers\7-Zip\(Default)", changes[1].SystemLocation);
        Assert.Equal(@"HKCR\Folder\shellex\ContextMenuHandlers\7-Zip\(Default)", changes[2].SystemLocation);
    }

    [Fact]
    public void CreateToggle_single_path_handler_without_AllRegistryPaths_works()
    {
        // Handler without AllRegistryPaths set (legacy compatibility)
        var handler = new ContextMenuHandler(
            Name: "Simple",
            Clsid: "{AAAA-BBBB}",
            RegistryPath: @"HKCR\*\shellex\ContextMenuHandlers\Simple",
            AppliesTo: "All files",
            DllPath: null,
            Publisher: null,
            IsEnabled: true);

        var changes = ContextMenuChangeFactory.CreateToggle(handler, enable: false);

        Assert.Single(changes);
        Assert.Equal(@"HKCR\*\shellex\ContextMenuHandlers\Simple\(Default)", changes[0].SystemLocation);
    }

    [Fact]
    public void CreateToggle_multi_path_mixed_state_uses_per_path_before_value()
    {
        // Path 1 enabled, path 2 disabled -- handler.IsEnabled = false (All must be enabled)
        var handler = new ContextMenuHandler(
            Name: "MixedHandler",
            Clsid: "{AAAA-BBBB-CCCC}",
            RegistryPath: @"HKCR\*\shellex\ContextMenuHandlers\Mixed",
            AppliesTo: "All files",
            DllPath: null,
            Publisher: null,
            IsEnabled: false,
            AllRegistryPaths:
            [
                @"HKCR\*\shellex\ContextMenuHandlers\Mixed",
                @"HKCR\Directory\shellex\ContextMenuHandlers\Mixed",
            ],
            AllScopes: ["All files", "Directories"],
            PathEnabledStates: new Dictionary<string, bool>
            {
                [@"HKCR\*\shellex\ContextMenuHandlers\Mixed"] = true,
                [@"HKCR\Directory\shellex\ContextMenuHandlers\Mixed"] = false,
            });

        var changes = ContextMenuChangeFactory.CreateToggle(handler, enable: false);

        // Path 1 was enabled -> BeforeValue should be clean CLSID
        Assert.Equal("{AAAA-BBBB-CCCC}", changes[0].BeforeValue);
        Assert.Equal("Enabled", changes[0].BeforeDisplay);

        // Path 2 was disabled -> BeforeValue should have dash prefix
        Assert.Equal("-{AAAA-BBBB-CCCC}", changes[1].BeforeValue);
        Assert.Equal("Disabled", changes[1].BeforeDisplay);
    }
}
