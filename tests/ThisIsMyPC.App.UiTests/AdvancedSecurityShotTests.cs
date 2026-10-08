using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Styling;
using ThisIsMyPC.App.UiTests.Fakes;
using ThisIsMyPC.App.ViewModels;
using ThisIsMyPC.App.Views;
using ThisIsMyPC.Core.Services;

namespace ThisIsMyPC.App.UiTests;

public sealed class AdvancedSecurityShotTests
{
    [AvaloniaFact]
    public void AdvancedCards_RenderInBothThemesAndStageLocalOverride()
    {
        var registry = new UiFakeRegistryService();
        registry.WriteString(@"HKLM\SOFTWARE\Microsoft\Windows NT\CurrentVersion", "EditionID", "Enterprise");
        var detector = new CapabilityDetector(registry);
        var pending = new PendingChangesService(capabilityDetector: detector);
        using var vm = new SecurityViewModel(registry, pending, detector);
        using var session = UiSession.ForView(new SecurityView(), vm, "security-advanced", width: 590, height: 580);
        var added = vm.Rows.Skip(28).ToList();
        Assert.Equal(24, added.Count);
        foreach (var theme in new[] { ThemeVariant.Dark, ThemeVariant.Light })
        {
            session.SetTheme(theme);
            foreach (var row in added)
            {
                vm.SearchText = row.Setting.Title;
                session.Pump();
                Assert.True(session.IsTextVisible(row.Setting.Title));
                Assert.True(row.IsControlEnabled);
                session.Screenshot($"{row.Setting.Id}-{theme.Key}");
            }
        }
        vm.SearchText = "Real-time protection preference source";
        session.Pump();
        session.Click(session.Find<ComboBox>(c => c.IsEffectivelyVisible));
        session.ClickText("Use local preference");
        session.Pump();
        var change = Assert.Single(Assert.Single(pending.PendingGroups).Changes);
        Assert.EndsWith(@"\LocalSettingOverrideDisableRealtimeMonitoring", change.SystemLocation, StringComparison.Ordinal);
        Assert.Equal("1", change.AfterValue);
        session.Screenshot("local-preference-staged");
    }
}
