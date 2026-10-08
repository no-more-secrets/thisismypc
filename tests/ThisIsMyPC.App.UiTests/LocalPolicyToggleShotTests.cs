using System.Collections.Immutable;
using Avalonia.Controls.Primitives;
using Avalonia.Headless.XUnit;
using Avalonia.Styling;
using ThisIsMyPC.App.UiTests.Fakes;
using ThisIsMyPC.App.ViewModels;
using ThisIsMyPC.App.Views;
using ThisIsMyPC.Core.Policies;
using ThisIsMyPC.Core.Services;
using ThisIsMyPC.Core.Sets;
using ThisIsMyPC.Modules.Annoyances.Models;
using ThisIsMyPC.Modules.Annoyances.Services;

namespace ThisIsMyPC.App.UiTests;

public sealed class LocalPolicyToggleShotTests
{
    [AvaloniaFact]
    public async Task SavedPolicy_ToggleStagesAndDiscardsWithoutSystemWrites()
    {
        var registry = new UiFakeRegistryService();
        registry.WriteString(@"HKLM\SOFTWARE\Microsoft\Windows NT\CurrentVersion", "EditionID", "Enterprise");
        registry.WriteDWord(LocalPolicyToggleCatalog.CloudKey, "DisableSoftLanding", 1);
        registry.WriteDWord(LocalPolicyToggleCatalog.CloudKey, "DisableWindowsConsumerFeatures", 1);
        var detector = new CapabilityDetector(registry);
        var policies = new PolicyControlStateReader(registry, Sources, detector);
        var pending = new PendingChangesService(policyStates: policies, capabilityDetector: detector);
        var reader = new AnnoyancesSettingsReader(registry);
        var scan = new AnnoyancesScanData(reader.ReadAll(), reader.ReadBingSearch(), reader.ReadSettingsSuggestedContent(),
            reader.ReadCopilotPolicy(), reader.ReadRecall(), reader.ReadLockScreenAds(), reader.ReadPreinstalledApps(),
            reader.ReadEdgeDebloat(), reader.ReadActivityHistory());
        using var vm = new AnnoyancesViewModel(scan, pending, registry, capabilityDetector: detector, policyStates: policies);
        using var session = UiSession.ForView(new SettingCardPageView(), vm, "editable-local-policy", width: 1100, height: 800);
        foreach (var theme in new[] { ThemeVariant.Dark, ThemeVariant.Light })
        {
            session.SetTheme(theme);
            session.Screenshot($"{theme.Key}-enabled");
        }
        foreach (var id in new[] { "windows-tips", "silent-app-installs", "consumer-features" })
        {
            var card = vm.CardGroups.SelectMany(g => g.Cards).Single(c => c.Model.SettingId == id);
            Assert.True(card.IsControlEnabled);
            Assert.True(card.IsEnabled);
            vm.SearchText = card.DisplayName;
            session.Pump();
            var toggle = session.Find<ToggleButton>(t => ReferenceEquals(t.DataContext, card) && t.IsVisible);
            session.Click(toggle);
            await session.WaitForAsync(() => pending.PendingCount > 0);
            Assert.False(card.IsEnabled);
            Assert.Single(pending.PendingGroups.SelectMany(g => g.Changes).Where(c => LocalPolicyValue.IsPolicyType(c.ValueType)));
            session.Screenshot(id + "-staged");
            session.Click(toggle);
            await session.WaitForAsync(() => pending.PendingCount == 0);
            Assert.True(card.IsEnabled);
        }
        Assert.Equal(1, registry.ReadDWord(LocalPolicyToggleCatalog.CloudKey, "DisableSoftLanding").Value);
        Assert.Equal(1, registry.ReadDWord(LocalPolicyToggleCatalog.CloudKey, "DisableWindowsConsumerFeatures").Value);
    }

    [AvaloniaFact]
    public void PresetWithSharedPolicy_ReportsConflictAndRefreshesPreview()
    {
        var registry = new UiFakeRegistryService();
        registry.WriteDWord(LocalPolicyToggleCatalog.CloudKey, "DisableWindowsConsumerFeatures", 1);
        registry.WriteString(@"HKLM\SOFTWARE\Microsoft\Windows NT\CurrentVersion", "EditionID", "Enterprise");
        var detector = new CapabilityDetector(registry);
        var policies = new PolicyControlStateReader(registry, Sources, detector);
        var pending = new PendingChangesService(policyStates: policies);
        var definition = new SetDefinition
        {
            Name = "Shared policy", Description = "Allow consumer features", Version = "1.0", Author = "Test",
            Category = SetCategory.TweakSet, Source = SetSource.BuiltIn, FilePath = "test.json",
            Entries = [new() { ModuleId = "Windows Annoyances", SettingId = "consumer-features", Value = "0", Description = "Allow" },
                new() { ModuleId = "Windows Annoyances", SettingId = "silent-app-installs", Value = "1", Description = "Allow" }],
        };
        using var vm = new SetLoaderViewModel(new() { Sets = [definition], Warnings = [] },
            [new AnnoyancesSetEntryInspector(registry, policies)], _ => new(true), pending, capabilityDetector: detector, policyStates: policies);
        vm.SelectSetCommand.Execute(vm.TweakSets[0]);
        foreach (var row in vm.PreviewGroups.SelectMany(g => g.Entries)) row.IsIncluded = true;
        vm.StageIncludedCommand.Execute(null);
        Assert.Single(pending.PendingGroups);
        Assert.Contains("already has a pending change", vm.StageMessage);
        Assert.Contains(vm.PreviewGroups.SelectMany(g => g.Entries), r => r.Resolution.PendingGroupId is not null);
    }

    private static IReadOnlyList<PolicySourceSnapshot> Sources() => [new("Computer", PolicyScope.Machine, PolicyFileStatus.Loaded,
        [new(LocalPolicyToggleCatalog.CloudKey[5..], "DisableSoftLanding", 4, BitConverter.GetBytes(1).ToImmutableArray()),
         new(LocalPolicyToggleCatalog.CloudKey[5..], "DisableWindowsConsumerFeatures", 4, BitConverter.GetBytes(1).ToImmutableArray())]),
        new("User", PolicyScope.User, PolicyFileStatus.Missing, [])];
}
