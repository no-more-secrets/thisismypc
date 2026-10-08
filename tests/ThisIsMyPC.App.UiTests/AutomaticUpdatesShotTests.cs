using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Styling;
using ThisIsMyPC.App.UiTests.Fakes;
using ThisIsMyPC.App.ViewModels;
using ThisIsMyPC.App.Views;
using ThisIsMyPC.Core.Policies;
using ThisIsMyPC.Core.Services;
using ThisIsMyPC.Modules.WindowsUpdate;
using ThisIsMyPC.Modules.WindowsUpdate.Services;

namespace ThisIsMyPC.App.UiTests;

public sealed class AutomaticUpdatesShotTests
{
    [AvaloniaTheory]
    [InlineData("Professional")]
    [InlineData("Core")]
    public void ChoicesStageAndDiscard(string edition)
    {
        var registry = new UiFakeRegistryService();
        registry.WriteString(@"HKLM\SOFTWARE\Microsoft\Windows NT\CurrentVersion", "EditionID", edition);
        registry.WriteDWord(WindowsUpdateRegistryPaths.AuPoliciesKeyPath, "NoAutoUpdate", 1);
        var detector = new CapabilityDetector(registry);
        var policies = new PolicyControlStateReader(registry, () => [new("Computer", PolicyScope.Machine, PolicyFileStatus.Loaded,
            [new(WindowsUpdateRegistryPaths.AuPoliciesKeyPath[5..], "NoAutoUpdate", 4, [1, 0, 0, 0])])], detector);
        var pending = new PendingChangesService(policyStates: policies, capabilityDetector: detector);
        using var vm = new WindowsUpdateViewModel(new WindowsUpdateSettingsReader(registry).ReadAll(), pending, registry,
            capabilityDetector: detector, policyStates: policies);
        using var session = UiSession.ForView(new SettingCardPageView(), vm, "automatic-updates", width: 1100, height: 800);
        var card = vm.CardGroups[0].Cards.Single(c => c.Model.SettingId == AutomaticUpdatesSetting.Id);
        var dropdown = session.Find<ComboBox>(c => ReferenceEquals(c.DataContext, card));
        Assert.Equal("Disabled", card.SelectedOption?.DisplayName);
        Assert.Equal(edition != "Core", dropdown.IsEnabled);
        foreach (var theme in new[] { ThemeVariant.Dark, ThemeVariant.Light })
        {
            session.SetTheme(theme);
            session.Screenshot(edition + "-" + theme.Key);
        }
        if (edition == "Core") return;
        session.Click(dropdown);
        session.Screenshot("options");
        session.ClickText("Enabled");
        session.Pump();
        Assert.Equal("0", LocalPolicyValue.Decode(Assert.Single(pending.PendingGroups).Changes[0].AfterValue)!.Live);
        session.Screenshot("staged");
        session.Click(dropdown);
        session.ClickText("Disabled");
        session.Pump();
        Assert.Empty(pending.PendingGroups);
        Assert.Equal("1", registry.ReadDWord(WindowsUpdateRegistryPaths.AuPoliciesKeyPath, "NoAutoUpdate").Value.ToString());
    }
}
