using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Styling;
using ThisIsMyPC.App.UiTests.Fakes;
using ThisIsMyPC.App.ViewModels;
using ThisIsMyPC.App.Views;
using ThisIsMyPC.Core.Services;

namespace ThisIsMyPC.App.UiTests;

public sealed class SecurityShotTests
{
    [AvaloniaTheory]
    [InlineData("Core")]
    [InlineData("Professional")]
    [InlineData("Enterprise")]
    public void EverySection_ShowsPolicyStatesAndEditionGating(string edition)
    {
        var registry = new UiFakeRegistryService();
        registry.WriteString(@"HKLM\SOFTWARE\Microsoft\Windows NT\CurrentVersion", "EditionID", edition);
        var detector = new CapabilityDetector(registry);
        var pending = new PendingChangesService(capabilityDetector: detector);
        using var vm = new SecurityViewModel(registry, pending, detector);
        var card = new Border { Margin = new Thickness(16), BorderThickness = new Thickness(1), Child = new SecurityView() };
        card.Bind(Border.BackgroundProperty, card.GetResourceObservable("RaisedBrush"));
        card.Bind(Border.BorderBrushProperty, card.GetResourceObservable("OutlineBrush"));
        using var session = UiSession.ForView(card, vm, "security", width: 1040, height: 850);
        Assert.Equal(16, vm.Rows.Count);
        foreach (var theme in new[] { ThemeVariant.Dark, ThemeVariant.Light })
        {
            session.SetTheme(theme);
            for (var index = 0; index < vm.Sections.Count; index++)
            {
                session.ClickText(vm.Sections[index].Header);
                session.Pump();
                Assert.Equal(index, vm.SelectedTabIndex);
                session.Screenshot($"{edition}-{index}-{theme.Key}");
            }
        }
        foreach (var row in vm.Rows)
            Assert.Equal(edition == "Enterprise" || (edition == "Professional" && row.Setting.Id == "secure-sign-in"), row.IsControlEnabled);
        vm.SearchText = "Ctrl+Alt+Delete";
        session.Pump();
        Assert.True(session.IsTextVisible("Requires Windows Pro or higher"));
        session.Screenshot($"{edition}-search");
    }

    [AvaloniaFact]
    public void Choices_ReplacePendingAndDiscardReturnsToLiveState()
    {
        var registry = new UiFakeRegistryService();
        registry.WriteString(@"HKLM\SOFTWARE\Microsoft\Windows NT\CurrentVersion", "EditionID", "Enterprise");
        var detector = new CapabilityDetector(registry);
        var pending = new PendingChangesService(capabilityDetector: detector);
        using var vm = new SecurityViewModel(registry, pending, detector);
        var row = vm.Rows.Single(r => r.Setting.Id == "smartscreen");
        row.SelectedOption = row.Choices.Single(o => o.Id == "warn");
        row.SelectedOption = row.Choices.Single(o => o.Id == "block");
        Assert.Single(pending.PendingGroups);
        Assert.Equal("Block", pending.PendingGroups[0].Changes[1].AfterValue);
        using var reopened = new SecurityViewModel(registry, pending, detector);
        Assert.Equal("block", reopened.Rows.Single(r => r.Setting.Id == "smartscreen").SelectedOption?.Id);
        pending.DiscardAll();
        Assert.Equal("default", row.SelectedOption?.Id);
        Assert.False(row.HasPendingChange);
    }

    [AvaloniaFact]
    public void NarrowPage_StagesUsingTheDropdown()
    {
        var registry = new UiFakeRegistryService();
        registry.WriteString(@"HKLM\SOFTWARE\Microsoft\Windows NT\CurrentVersion", "EditionID", "Enterprise");
        var detector = new CapabilityDetector(registry);
        var pending = new PendingChangesService(capabilityDetector: detector);
        using var vm = new SecurityViewModel(registry, pending, detector);
        using var session = UiSession.ForView(new SecurityView(), vm, "security-narrow", width: 590, height: 650);
        var combo = session.Find<ComboBox>(_ => true);
        session.Click(combo);
        session.ClickText("On");
        session.Pump();
        Assert.Single(pending.PendingGroups);
        Assert.Equal("0", pending.PendingGroups[0].Changes[0].AfterValue);
        session.Screenshot("staged-sign-in");
        session.ClickText("App protection");
        session.Screenshot("app-protection");
    }
}
