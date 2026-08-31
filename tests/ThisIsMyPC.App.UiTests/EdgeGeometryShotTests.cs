using Avalonia.Headless.XUnit;
using ThisIsMyPC.App.ViewModels;

namespace ThisIsMyPC.App.UiTests;

/// <summary>
/// Captures the pages the full walkthrough reaches last (card pages, Software,
/// Settings) so edge-geometry parity can be measured without sitting through a
/// slow Display DDC scan. Boots the real MainWindow, so Category=Diagnostic.
/// </summary>
[Trait("Category", "Diagnostic")]
public class EdgeGeometryShotTests
{
    [AvaloniaFact(Timeout = 300_000)]
    public async Task CaptureCardPagesSoftwareAndSettings()
    {
        using var session = UiSession.ForMainWindow("edge-geometry");
        var viewModel = (MainWindowViewModel)session.Window.DataContext!;

        await session.WaitForAsync(
            () => viewModel.SidebarGroups.Count > 0, timeoutMs: 30_000, what: "sidebar population");

        foreach (var name in new[] { "Windows Annoyances", "Windows Update", "Privacy & Telemetry", "Software" })
        {
            session.ClickText(name);
            await session.WaitForAsync(
                () => viewModel.CurrentContent is not null && viewModel.ContentTitle == name,
                timeoutMs: 120_000,
                what: $"'{name}' content load");
            session.Screenshot(Slug(name));
        }

        session.ClickText("Settings");
        await session.WaitForAsync(
            () => viewModel.ContentTitle == "Settings", timeoutMs: 30_000, what: "settings load");
        session.Screenshot("settings");
    }

    private static string Slug(string name) =>
        new(name.Select(c => char.IsLetterOrDigit(c) ? char.ToLowerInvariant(c) : '-').ToArray());
}
