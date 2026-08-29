using ThisIsMyPC.Core.Changes;
using ThisIsMyPC.Modules.WindowsUpdate;
using ThisIsMyPC.Modules.WindowsUpdate.Changes;
using ThisIsMyPC.Modules.WindowsUpdate.Services;
using ThisIsMyPC.Modules.WindowsUpdate.Tests.Fakes;

namespace ThisIsMyPC.Modules.WindowsUpdate.Tests;

public class WindowsUpdateChangeFactoryTests
{
    private static WindowsUpdateSettingsReader DefaultReader() =>
        new(new FakeRegistryService());

    [Fact]
    public void CreateToggle_Configure_WritesConfiguredValue_WithGPCacheEnforcementBothDirections()
    {
        var setting = DefaultReader().ReadSingles().Single(s => s.Id == "no-auto-reboot");

        var configure = WindowsUpdateChangeFactory.CreateToggle(setting, configure: true);
        var restore = WindowsUpdateChangeFactory.CreateToggle(setting, configure: false);

        Assert.Equal("1", configure.AfterValue);
        Assert.Equal(string.Empty, restore.AfterValue); // empty = delete (Not configured)
        Assert.Equal(string.Empty, configure.BeforeValue); // live scan: value absent

        // GPCache clear must run for BOTH directions — the orchestrator's cache would
        // otherwise keep a removed policy alive after revert.
        foreach (var change in new[] { configure, restore })
        {
            Assert.NotNull(change.Enforcement);
            Assert.Equal(
                [WindowsUpdateRegistryPaths.GPCacheKeyPath],
                change.Enforcement!.GPCacheEntries);
        }
    }

    [Fact]
    public void CreateToggle_DeliveryOptimization_CarriesNoEnforcement()
    {
        var setting = DefaultReader().ReadSingles().Single(s => s.Id == "delivery-optimization");

        var change = WindowsUpdateChangeFactory.CreateToggle(setting, configure: true, gpCache: false);

        Assert.Equal("0", change.AfterValue);
        Assert.Null(change.Enforcement);
    }

    [Fact]
    public void GPCachePath_SatisfiesTheExecutorPathGuard()
    {
        // EnforcementExecutor.IsSafeGPCachePath contract: hive-rooted, at least three
        // levels below the hive, and a literal "GPCache" segment.
        var segments = WindowsUpdateRegistryPaths.GPCacheKeyPath.Split('\\');

        Assert.Equal("HKLM", segments[0]);
        Assert.True(segments.Length >= 4, "path must be at least three levels below the hive");
        Assert.Contains("GPCache", segments, StringComparer.OrdinalIgnoreCase);
    }

    [Fact]
    public void CreateVersionPinGroup_ThreeAtomicChanges_SharingSettingId()
    {
        var registry = new FakeRegistryService();
        registry.SetString(WindowsUpdateRegistryPaths.CurrentVersionKeyPath, "DisplayVersion", "24H2");
        var pin = new WindowsUpdateSettingsReader(registry).ReadVersionPin();

        var group = WindowsUpdateChangeFactory.CreateVersionPinGroup(pin, configure: true);

        Assert.NotNull(group);
        Assert.Equal(3, group!.Changes.Count);
        Assert.All(group.Changes, c => Assert.Equal("version-pin", c.SettingId));
        Assert.All(group.Changes, c => Assert.NotNull(c.Enforcement));
        Assert.Equal("1", group.Changes[0].AfterValue);
        Assert.Equal("Windows 11", group.Changes[1].AfterValue);
        Assert.Equal("24H2", group.Changes[2].AfterValue);
    }

    [Fact]
    public void CreateVersionPinGroup_Restore_DeletesAllThree()
    {
        var registry = new FakeRegistryService();
        registry.SetString(WindowsUpdateRegistryPaths.CurrentVersionKeyPath, "DisplayVersion", "24H2");
        var pin = new WindowsUpdateSettingsReader(registry).ReadVersionPin();

        var group = WindowsUpdateChangeFactory.CreateVersionPinGroup(pin, configure: false);

        Assert.NotNull(group);
        Assert.All(group!.Changes, c => Assert.Equal(string.Empty, c.AfterValue));
    }

    [Fact]
    public void CreateVersionPinGroup_EmptyPin_ReturnsNull()
    {
        Assert.Null(WindowsUpdateChangeFactory.CreateVersionPinGroup([], configure: true));
    }
}
