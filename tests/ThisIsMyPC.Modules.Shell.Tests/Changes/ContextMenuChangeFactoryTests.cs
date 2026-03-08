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

    [Fact]
    public void CreateToggle_enable_removes_dash_prefix()
    {
        var handler = MakeHandler(isEnabled: false);
        var change = ContextMenuChangeFactory.CreateToggle(handler, enable: true);

        Assert.Equal("{12345678-1234-1234-1234-123456789ABC}", change.AfterValue);
        Assert.Equal("-{12345678-1234-1234-1234-123456789ABC}", change.BeforeValue);
        Assert.Equal(ChangeCategory.Enable, change.Category);
    }

    [Fact]
    public void CreateToggle_disable_adds_dash_prefix()
    {
        var handler = MakeHandler(isEnabled: true);
        var change = ContextMenuChangeFactory.CreateToggle(handler, enable: false);

        Assert.Equal("-{12345678-1234-1234-1234-123456789ABC}", change.AfterValue);
        Assert.Equal("{12345678-1234-1234-1234-123456789ABC}", change.BeforeValue);
        Assert.Equal(ChangeCategory.Disable, change.Category);
    }

    [Fact]
    public void CreateToggle_sets_correct_system_location()
    {
        var handler = MakeHandler();
        var change = ContextMenuChangeFactory.CreateToggle(handler, enable: true);

        Assert.Equal(@"HKCR\*\shellex\ContextMenuHandlers\TestHandler\(Default)", change.SystemLocation);
    }

    [Fact]
    public void CreateToggle_uses_Registry_String_value_type()
    {
        var handler = MakeHandler();
        var change = ContextMenuChangeFactory.CreateToggle(handler, enable: true);

        Assert.Equal(ChangeValueType.Registry_String, change.ValueType);
    }

    [Fact]
    public void CreateToggle_sets_module_id()
    {
        var handler = MakeHandler();
        var change = ContextMenuChangeFactory.CreateToggle(handler, enable: true);

        Assert.Equal("Shell & Explorer", change.ModuleId);
    }
}
