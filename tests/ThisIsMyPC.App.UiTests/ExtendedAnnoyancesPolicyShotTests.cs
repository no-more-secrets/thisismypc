using System.Collections.Immutable;
using Avalonia.Controls.Primitives;
using Avalonia.Headless.XUnit;
using Avalonia.Styling;
using ThisIsMyPC.App.UiTests.Fakes;
using ThisIsMyPC.App.ViewModels;
using ThisIsMyPC.App.Views;
using ThisIsMyPC.Core.Policies;
using ThisIsMyPC.Core.Services;
using ThisIsMyPC.Modules.Annoyances.Models;
using ThisIsMyPC.Modules.Annoyances.Services;

namespace ThisIsMyPC.App.UiTests;

public sealed class ExtendedAnnoyancesPolicyShotTests
{
    [AvaloniaFact]
    public async Task MachineAndInheritedUserPolicies_AllControlsStageAndDiscard()
    {
        var registry = new UiFakeRegistryService();
        registry.WriteString(@"HKLM\SOFTWARE\Microsoft\Windows NT\CurrentVersion", "EditionID", "Enterprise");
        var detector = new CapabilityDetector(registry);
        var ids = new[] { "windows-tips", "consumer-features", "silent-app-installs", "welcome-experience", "app-suggestions",
            "settings-suggested-content", "tailored-experiences", "advertising-id", "feedback-frequency", "dynamic-search-box",
            "game-dvr", "spotlight-features", "spotlight-collection-desktop", "edge-sidebar", "edge-shortcuts", "bing-search", "copilot",
            "activity-history", "edge-debloat", "preinstalled-apps" };
        var targets = ids.SelectMany(id => LocalPolicyToggleCatalog.Targets("Windows Annoyances", id)).DistinctBy(t => t.Location).ToArray();
        foreach (var target in targets)
        {
            var split = target.Location.LastIndexOf('\\');
            registry.WriteDWord(target.Location[..split], target.Location[(split + 1)..], int.Parse(target.Suppressed));
        }
        PolicySourceSnapshot Source(bool user) => new(user ? "Common user policy" : "Computer", user ? PolicyScope.User : PolicyScope.Machine,
            PolicyFileStatus.Loaded, targets.Where(t => t.Location.StartsWith(user ? "HKCU" : "HKLM", StringComparison.Ordinal))
                .Select(t => new RegistryPolicyEntry(t.Location[5..t.Location.LastIndexOf('\\')], t.Location[(t.Location.LastIndexOf('\\') + 1)..],
                    4, BitConverter.GetBytes(int.Parse(t.Suppressed)).ToImmutableArray())).ToImmutableArray());
        var policies = new PolicyControlStateReader(registry, () => [Source(false), Source(true),
            new("Account", PolicyScope.User, PolicyFileStatus.Missing, []) { IsAccountPolicy = true }], detector);
        var pending = new PendingChangesService(policyStates: policies, capabilityDetector: detector);
        var reader = new AnnoyancesSettingsReader(registry);
        var scan = new AnnoyancesScanData(reader.ReadAll(), reader.ReadBingSearch(), reader.ReadSettingsSuggestedContent(), reader.ReadCopilotPolicy(),
            reader.ReadRecall(), reader.ReadLockScreenAds(), reader.ReadPreinstalledApps(), reader.ReadEdgeDebloat(), reader.ReadActivityHistory());
        using var vm = new AnnoyancesViewModel(scan, pending, registry, capabilityDetector: detector, policyStates: policies);
        using var session = UiSession.ForView(new SettingCardPageView(), vm, "expanded-annoyances-policy", width: 1100, height: 800);
        foreach (var theme in new[] { ThemeVariant.Dark, ThemeVariant.Light })
        {
            session.SetTheme(theme);
            for (var tab = 0; tab < vm.CardGroups.Count; tab++)
            {
                vm.SelectedTabIndex = tab;
                session.Pump();
                session.Screenshot($"{theme.Key}-{tab}");
            }
        }
        foreach (var id in ids)
        {
            var card = vm.CardGroups.SelectMany(group => group.Cards).Single(c => c.Model.SettingId == id);
            Assert.True(card.IsControlEnabled, id + ": " + card.PolicyStateText);
            var before = card.IsEnabled;
            vm.SearchText = card.DisplayName;
            session.Pump();
            var toggle = session.Find<ToggleButton>(t => ReferenceEquals(t.DataContext, card) && t.IsVisible);
            session.Click(toggle);
            await session.WaitForAsync(() => pending.PendingCount > 0, what: id);
            Assert.NotEqual(before, card.IsEnabled);
            Assert.Contains(pending.PendingGroups.SelectMany(g => g.Changes), c => LocalPolicyValue.IsPolicyType(c.ValueType));
            session.Click(toggle);
            await session.WaitForAsync(() => pending.PendingCount == 0, what: id + " discard");
            Assert.Equal(before, card.IsEnabled);
        }
    }
}
