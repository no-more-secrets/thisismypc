using ThisIsMyPC.Core.Services;
using ThisIsMyPC.Modules.Annoyances;
using ThisIsMyPC.Modules.Annoyances.Changes;
using ThisIsMyPC.Modules.Annoyances.Models;
using ThisIsMyPC.Modules.Annoyances.Services;
using ThisIsMyPC.Modules.Annoyances.Tests.Fakes;

namespace ThisIsMyPC.Modules.Annoyances.Tests;

public sealed class AdvertisingAndTrackingTests
{
    private readonly FakeRegistryService _registry = new();

    private AnnoyancesSettingsReader Reader => new(_registry);

    [Fact]
    public void AdvertisingId_TargetsPerUserKey_NullEnforcement()
    {
        var pref = Reader.ReadAll().Single(p => p.Id == "advertising-id");

        Assert.Equal(AnnoyanceSection.AdvertisingAndTracking, pref.Section);
        Assert.Equal(@"HKCU\Software\Microsoft\Windows\CurrentVersion\AdvertisingInfo", pref.RegistryKeyPath);
        Assert.Equal("Enabled", pref.RegistryValueName);

        var change = AnnoyanceChangeFactory.CreateToggle(pref, suppress: true);
        Assert.Equal(
            @"HKCU\Software\Microsoft\Windows\CurrentVersion\AdvertisingInfo\Enabled",
            change.SystemLocation);
        Assert.Equal("0", change.AfterValue);
        Assert.Null(change.Enforcement); // FR137: zero-enforcement quick toggle
    }

    [Fact]
    public void ActivityHistory_TargetsHklmSystemPolicy()
    {
        var pref = Reader.ReadAll().Single(p => p.Id == "activity-history");

        Assert.Equal(@"HKLM\SOFTWARE\Policies\Microsoft\Windows\System", pref.RegistryKeyPath);
        Assert.Equal("EnableActivityFeed", pref.RegistryValueName);
        Assert.False(pref.IsSuppressed); // missing policy = feed on
    }

    [Fact]
    public void SettingsSuggestedContent_ThreeValuesOneGroup_SharedSettingId()
    {
        var group = AnnoyanceChangeFactory.CreateGroupToggle(
            Reader.ReadSettingsSuggestedContent(),
            settingId: "settings-suggested-content",
            displayName: "Suggested content in Settings",
            description: "d",
            suppress: true);

        Assert.Equal(3, group.Changes.Count);
        Assert.All(group.Changes, c => Assert.Equal("settings-suggested-content", c.SettingId));
        Assert.All(group.Changes, c => Assert.Equal("0", c.AfterValue));
        Assert.All(group.Changes, c => Assert.Null(c.Enforcement));
        Assert.Equal(
            ["SubscribedContent-338393Enabled", "SubscribedContent-353694Enabled", "SubscribedContent-353696Enabled"],
            group.Changes.Select(c => c.SystemLocation.Split('\\')[^1]));
    }

    [Fact]
    public async Task SuggestedContentGroup_AppliesAllThreeValues()
    {
        var module = new AnnoyancesModule(_registry);
        var pendingChanges = new PendingChangesService();
        pendingChanges.Stage(AnnoyanceChangeFactory.CreateGroupToggle(
            Reader.ReadSettingsSuggestedContent(), "settings-suggested-content", "n", "d", suppress: true));

        var result = await pendingChanges.ApplyAllAsync(module.ApplyChangeAsync, module.RevertChangeAsync);

        Assert.True(result.IsSuccess, result.ErrorMessage);
        foreach (var value in new[]
        {
            "SubscribedContent-338393Enabled", "SubscribedContent-353694Enabled", "SubscribedContent-353696Enabled",
        })
        {
            Assert.Equal(0, _registry.ReadDWord(
                AnnoyancesRegistryPaths.ContentDeliveryManagerKeyPath, value).Value);
        }
        Assert.All(Reader.ReadSettingsSuggestedContent(), p => Assert.True(p.IsSuppressed));

        // Restore direction: all three back to the Windows default
        pendingChanges.Stage(AnnoyanceChangeFactory.CreateGroupToggle(
            Reader.ReadSettingsSuggestedContent(), "settings-suggested-content", "n", "d", suppress: false));
        var restore = await pendingChanges.ApplyAllAsync(module.ApplyChangeAsync, module.RevertChangeAsync);

        Assert.True(restore.IsSuccess, restore.ErrorMessage);
        Assert.All(Reader.ReadSettingsSuggestedContent(), p => Assert.False(p.IsSuppressed));
    }
}
