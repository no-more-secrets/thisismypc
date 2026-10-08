using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Styling;
using ThisIsMyPC.App.UiTests.Fakes;
using ThisIsMyPC.App.ViewModels;
using ThisIsMyPC.App.Views;
using ThisIsMyPC.Core.Policies;
using ThisIsMyPC.Core.Services;
using ThisIsMyPC.Modules.Privacy;
using ThisIsMyPC.Modules.Privacy.Services;

namespace ThisIsMyPC.App.UiTests;

public class PrivacyChoiceShotTests
{
    [AvaloniaTheory]
    [InlineData("Professional", 1)]
    [InlineData("Enterprise", 0)]
    [InlineData("Education", 0)]
    [InlineData("Core", 1)]
    public void ChoicesAndTechnicalDetails(string edition, int current)
    {
        var registry = new UiFakeRegistryService();
        registry.WriteString(@"HKLM\SOFTWARE\Microsoft\Windows NT\CurrentVersion", "EditionID", edition);
        registry.WriteDWord(PrivacyRegistryPaths.DataCollectionPoliciesKeyPath, "AllowTelemetry", current);
        registry.WriteDWord(PrivacyRegistryPaths.LocationPoliciesKeyPath, "DisableLocation", 1);
        var detector = new CapabilityDetector(registry);
        var policies = new PolicyControlStateReader(registry, () =>
            [new("Computer", PolicyScope.Machine, PolicyFileStatus.Loaded,
                [new(PrivacyRegistryPaths.DataCollectionPoliciesKeyPath[5..], "AllowTelemetry", 4, [checked((byte)current), 0, 0, 0])]),
             new("User", PolicyScope.User, PolicyFileStatus.Missing, [])], detector);
        var pending = new PendingChangesService(capabilityDetector: detector);
        using var vm = new PrivacyViewModel(new PrivacySettingsReader(registry).ReadAll(), pending, registry,
            capabilityDetector: detector, policyStates: policies);
        vm.IsCompact = true;
        using var session = UiSession.ForView(new SettingCardPageView(), vm, "privacy-choices", width: 976, height: 680);
        var card = vm.CardGroups[0].Cards[0];
        var dropdown = session.Find<ComboBox>(_ => true);
        Assert.Equal(DiagnosticDataSetting.Display(current.ToString(System.Globalization.CultureInfo.InvariantCulture)), card.SelectedOption?.DisplayName);
        Assert.Equal(edition is "Enterprise" or "Education", card.Options.Any(o => o.Value == "0"));
        Assert.Equal(edition != "Core", dropdown.IsEnabled);
        foreach (var theme in new[] { ThemeVariant.Dark, ThemeVariant.Light })
        {
            session.SetTheme(theme);
            Assert.False(session.IsTextVisible("Current policy value: " + current + "."));
            session.Screenshot(edition + "-" + theme.Key);
        }
        if (edition != "Core")
        {
            dropdown.SelectedItem = card.Options.Single(o => o.Value == "3");
            Assert.Single(pending.PendingGroups);
            Assert.Equal("3", LocalPolicyValue.Decode(pending.PendingGroups[0].Changes[0].AfterValue)!.Live);
            dropdown.SelectedItem = card.Options.Single(o => o.Value == "");
            Assert.Single(pending.PendingGroups);
            session.Screenshot(edition + "-pending");
            pending.DiscardAll();
            session.Pump();
            Assert.Equal(current.ToString(System.Globalization.CultureInfo.InvariantCulture), card.SelectedOption?.Value);
        }
        session.ClickText("Permissions & Tracking");
        var location = vm.CardGroups[1].Cards.First(c => c.Model.SettingId == "location");
        Assert.False(session.IsTextVisible("Current policy value: 1."));
        session.Screenshot(edition + "-permissions");
        session.Click(session.Find<CheckBox>(c => c.Content as string == "Technical details"));
        Assert.Contains("Current policy value: 1.", location.PolicyStateText);
        Assert.True(session.IsTextVisible(location.PolicyStateText!));
        session.Screenshot(edition + "-details");
    }
}
