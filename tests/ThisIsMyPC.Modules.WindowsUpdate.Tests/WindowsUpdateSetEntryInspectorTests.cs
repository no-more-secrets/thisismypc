using ThisIsMyPC.Core.Sets;
using ThisIsMyPC.Modules.WindowsUpdate;
using ThisIsMyPC.Modules.WindowsUpdate.Services;
using ThisIsMyPC.Modules.WindowsUpdate.Tests.Fakes;

namespace ThisIsMyPC.Modules.WindowsUpdate.Tests;

public class WindowsUpdateSetEntryInspectorTests
{
    private static SetEntry Entry(string settingId, string value) => new()
    {
        ModuleId = "Windows Update",
        SettingId = settingId,
        Value = value,
        Description = "test",
    };

    private static FakeRegistryService RegistryWithDisplayVersion()
    {
        var registry = new FakeRegistryService();
        registry.SetString(WindowsUpdateRegistryPaths.CurrentVersionKeyPath, "DisplayVersion", "24H2");
        return registry;
    }

    [Fact]
    public void Inspect_UnknownSettingId_ReturnsNull()
    {
        var inspector = new WindowsUpdateSetEntryInspector(new FakeRegistryService());
        Assert.Null(inspector.Inspect(Entry("no-such-setting", "1")));
    }

    [Fact]
    public void Inspect_Single_DefaultMachine_NotConfigured_NotApplied()
    {
        var inspector = new WindowsUpdateSetEntryInspector(new FakeRegistryService());

        var state = inspector.Inspect(Entry("no-auto-reboot", "1"));

        Assert.NotNull(state);
        Assert.Equal("Not configured", state!.CurrentDisplay);
        Assert.False(state.IsApplied);
    }

    [Fact]
    public void Inspect_Single_ConfiguredMachine_IsApplied()
    {
        var registry = new FakeRegistryService();
        registry.SetDWord(WindowsUpdateRegistryPaths.AuPoliciesKeyPath, "NoAutoRebootWithLoggedOnUsers", 1);
        var inspector = new WindowsUpdateSetEntryInspector(registry);

        var state = inspector.Inspect(Entry("no-auto-reboot", "1"));

        Assert.NotNull(state);
        Assert.Equal("Configured", state!.CurrentDisplay);
        Assert.True(state.IsApplied);
    }

    [Fact]
    public void Inspect_VersionPin_NoDisplayVersion_ReturnsNull()
    {
        var inspector = new WindowsUpdateSetEntryInspector(new FakeRegistryService());
        Assert.Null(inspector.Inspect(Entry("version-pin", "1")));
    }

    [Fact]
    public void Inspect_VersionPin_PartiallySet_NotApplied()
    {
        var registry = RegistryWithDisplayVersion();
        registry.SetDWord(WindowsUpdateRegistryPaths.WindowsUpdatePoliciesKeyPath, "TargetReleaseVersion", 1);
        var inspector = new WindowsUpdateSetEntryInspector(registry);

        var state = inspector.Inspect(Entry("version-pin", "1"));

        Assert.NotNull(state);
        Assert.Equal("Partially set", state!.CurrentDisplay);
        Assert.False(state.IsApplied);
    }

    [Fact]
    public void Inspect_VersionPin_FullyPinned_IsApplied()
    {
        var registry = RegistryWithDisplayVersion();
        registry.SetDWord(WindowsUpdateRegistryPaths.WindowsUpdatePoliciesKeyPath, "TargetReleaseVersion", 1);
        registry.SetString(WindowsUpdateRegistryPaths.WindowsUpdatePoliciesKeyPath, "ProductVersion", "Windows 11");
        registry.SetString(WindowsUpdateRegistryPaths.WindowsUpdatePoliciesKeyPath, "TargetReleaseVersionInfo", "24H2");
        var inspector = new WindowsUpdateSetEntryInspector(registry);

        var state = inspector.Inspect(Entry("version-pin", "1"));

        Assert.NotNull(state);
        Assert.True(state!.IsApplied);
    }

    [Fact]
    public void CreateChangeGroup_Single_ConfigureDirection()
    {
        var inspector = new WindowsUpdateSetEntryInspector(new FakeRegistryService());

        var group = inspector.CreateChangeGroup(Entry("exclude-drivers", "1"));

        Assert.NotNull(group);
        var change = Assert.Single(group!.Changes);
        Assert.Equal("1", change.AfterValue);
        Assert.NotNull(change.Enforcement);
    }

    [Fact]
    public void CreateChangeGroup_DeliveryOptimization_SkuTagButNoGPCache()
    {
        var inspector = new WindowsUpdateSetEntryInspector(new FakeRegistryService());

        var group = inspector.CreateChangeGroup(Entry("delivery-optimization", "0"));

        Assert.NotNull(group);
        var enforcement = Assert.Single(group!.Changes).Enforcement;
        Assert.NotNull(enforcement);
        Assert.Null(enforcement!.GPCacheEntries);
        Assert.Equal(Core.Modules.WindowsSku.Pro, enforcement.SkuRestriction);
    }

    [Fact]
    public void CreateChangeGroup_EmptyValue_RestoresToNotConfigured()
    {
        var registry = new FakeRegistryService();
        registry.SetDWord(WindowsUpdateRegistryPaths.AuPoliciesKeyPath, "AUOptions", 2);
        var inspector = new WindowsUpdateSetEntryInspector(registry);

        var group = inspector.CreateChangeGroup(Entry("auto-update-mode", ""));

        Assert.NotNull(group);
        var change = Assert.Single(group!.Changes);
        Assert.Equal(string.Empty, change.AfterValue);
        Assert.Equal("2", change.BeforeValue);
    }

    [Fact]
    public void CreateChangeGroup_BogusValue_ReturnsNull()
    {
        var inspector = new WindowsUpdateSetEntryInspector(new FakeRegistryService());
        Assert.Null(inspector.CreateChangeGroup(Entry("no-auto-reboot", "banana")));
    }

    [Fact]
    public void CreateChangeGroup_VersionPin_ThreeChanges()
    {
        var inspector = new WindowsUpdateSetEntryInspector(RegistryWithDisplayVersion());

        var group = inspector.CreateChangeGroup(Entry("version-pin", "1"));

        Assert.NotNull(group);
        Assert.Equal(3, group!.Changes.Count);
        Assert.Equal("24H2", group.Changes[2].AfterValue);
    }

    [Fact]
    public void CreateChangeGroup_VersionPin_NoDisplayVersion_ReturnsNull()
    {
        var inspector = new WindowsUpdateSetEntryInspector(new FakeRegistryService());
        Assert.Null(inspector.CreateChangeGroup(Entry("version-pin", "1")));
    }
}
