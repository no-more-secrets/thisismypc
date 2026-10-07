using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Styling;
using ThisIsMyPC.App.ViewModels;
using ThisIsMyPC.Interop.Win32.Registry;
using ThisIsMyPC.Modules.Privacy;
using ThisIsMyPC.Modules.WindowsUpdate;

namespace ThisIsMyPC.App.UiTests;

[Trait("Category", "Diagnostic")]
public sealed class PresetPolicyLiveShotTests
{
    [AvaloniaFact(Timeout = 180_000)]
    public async Task PresetsRecognizeExistingPoliciesWithoutStagingChanges()
    {
        using var session = UiSession.ForMainWindow("preset-policy-live");
        session.Window.Width = 1200;
        var main = (MainWindowViewModel)session.Window.DataContext!;
        await session.WaitForAsync(() => main.SidebarGroups.Count > 0, what: "sidebar");
        session.ClickText("Presets");
        await session.WaitForAsync(() => main.CurrentContent is SetLoaderViewModel, what: "Presets");
        var vm = (SetLoaderViewModel)main.CurrentContent!;
        var registry = new RegistryService();
        foreach (var theme in new[] { ThemeVariant.Dark, ThemeVariant.Light })
        foreach (var preset in new[] { "Privacy Baseline", "Windows Update Control" })
        {
            session.SetTheme(theme);
            var card = session.Find<TextBlock>(t => t.Text == preset);
            card.BringIntoView();
            session.Pump();
            session.Click(card);
            var privacy = preset == "Privacy Baseline";
            var row = vm.PreviewGroups.SelectMany(g => g.Entries)
                .Single(r => r.Entry.SettingId == (privacy ? "telemetry-level" : "auto-update-mode"));
            var livePolicy = registry.ReadDWord(privacy ? PrivacyRegistryPaths.DataCollectionPoliciesKeyPath
                : WindowsUpdateRegistryPaths.AuPoliciesKeyPath, privacy ? "AllowTelemetry" : "NoAutoUpdate");
            if (livePolicy.IsSuccess && livePolicy.Value == (privacy ? 0 : 1))
            {
                Assert.True(row.IsApplied);
                Assert.Equal("Already covered", row.AppliedLabel);
                Assert.False(row.IsIncluded);
                Assert.False(row.CanToggle);
            }
            session.Type(session.Find<TextBox>(b => b.Name == "PresetSearch"), row.Entry.SettingId);
            session.Screenshot($"{theme.Key}-{(privacy ? "privacy" : "updates")}");
        }
    }
}
