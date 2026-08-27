using ThisIsMyPC.Core.Changes;
using ThisIsMyPC.Modules.Annoyances;
using ThisIsMyPC.Modules.Annoyances.Changes;
using ThisIsMyPC.Modules.Annoyances.Services;
using ThisIsMyPC.Modules.Annoyances.Tests.Fakes;

namespace ThisIsMyPC.Modules.Annoyances.Tests;

public sealed class AnnoyanceChangeFactoryTests
{
    private readonly FakeRegistryService _registry = new();

    [Fact]
    public void SuppressToggle_TargetsExactKeyWithNullEnforcement()
    {
        var scoobe = new AnnoyancesSettingsReader(_registry).ReadAll().Single(p => p.Id == "scoobe-nags");

        var change = AnnoyanceChangeFactory.CreateToggle(scoobe, suppress: true);

        Assert.Equal("Windows Annoyances", change.ModuleId);
        Assert.Equal("scoobe-nags", change.SettingId);
        Assert.Equal(
            @"HKCU\Software\Microsoft\Windows\CurrentVersion\UserProfileEngagement\ScoobeSystemSettingEnabled",
            change.SystemLocation);
        Assert.Equal("0", change.AfterValue);
        Assert.Equal(ChangeValueType.Registry_DWord, change.ValueType);
        Assert.Null(change.Enforcement); // FR139: zero enforcement complexity
        Assert.Equal(RestartRequirement.None, change.RestartRequirement);
    }

    [Fact]
    public void BeforeValue_UsesLiveCurrentValue_NotToggleDirection()
    {
        // A quirky existing value (e.g. 2) must be captured verbatim for revert fidelity.
        _registry.SetDWord(AnnoyancesRegistryPaths.ContentDeliveryManagerKeyPath, "SubscribedContent-338388Enabled", 2);
        var pref = new AnnoyancesSettingsReader(_registry).ReadAll().Single(p => p.Id == "app-suggestions");

        var change = AnnoyanceChangeFactory.CreateToggle(pref, suppress: true);

        Assert.Equal("2", change.BeforeValue);
        Assert.Equal("0", change.AfterValue);
    }

    [Fact]
    public void UnsuppressToggle_RestoresWindowsDefault()
    {
        _registry.SetDWord(AnnoyancesRegistryPaths.ContentDeliveryManagerKeyPath, "SystemPaneSuggestionsEnabled", 0);
        var pref = new AnnoyancesSettingsReader(_registry).ReadAll().Single(p => p.Id == "settings-suggestions");

        var change = AnnoyanceChangeFactory.CreateToggle(pref, suppress: false);

        Assert.Equal("0", change.BeforeValue);
        Assert.Equal("1", change.AfterValue);
        Assert.Equal(ChangeCategory.Enable, change.Category);
    }
}
