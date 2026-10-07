using ThisIsMyPC.Core.Sets;
using ThisIsMyPC.Modules.WindowsUpdate.Services;
using ThisIsMyPC.Modules.WindowsUpdate.Tests.Fakes;

namespace ThisIsMyPC.Modules.WindowsUpdate.Tests;

public sealed class DisabledAutomaticUpdatePolicyTests
{
    [Theory]
    [InlineData(1, true)]
    [InlineData(0, false)]
    [InlineData(2, false)]
    public void DisabledAutomaticUpdatesCoverTheNotificationPreset(int noAutoUpdate, bool covered)
    {
        var registry = new FakeRegistryService();
        registry.SetDWord(WindowsUpdateRegistryPaths.AuPoliciesKeyPath, "NoAutoUpdate", noAutoUpdate);
        var inspector = new WindowsUpdateSetEntryInspector(registry);
        var entry = new SetEntry { ModuleId = inspector.ModuleId, SettingId = "auto-update-mode", Value = "2", Description = "Notify" };
        var state = inspector.Inspect(entry)!;
        Assert.Equal(covered, state.IsApplied);
        Assert.Equal(covered, state.CoveredByPolicy is not null);
        Assert.Empty(state.CurrentValue);
        Assert.Null(inspector.Inspect(entry with { Value = "" })!.CoveredByPolicy);
        Assert.False(inspector.Inspect(entry with { SettingId = "exclude-drivers", Value = "1" })!.IsApplied);
    }
}
