using Avalonia.Headless.XUnit;
using ThisIsMyPC.App.ViewModels;
using ThisIsMyPC.App.Views;
using ThisIsMyPC.Core.Services;
using ThisIsMyPC.Modules.Software.Models;
using ThisIsMyPC.Modules.Software.Services;

namespace ThisIsMyPC.App.UiTests;

/// <summary>
/// CI-safe: renders the Software view with catalog data and fake queue state,
/// drives it with real mouse clicks, and drops screenshots under
/// artifacts/ui-shots/software-view/.
/// </summary>
public class SoftwareViewShotTests
{
    private static SoftwareScanData CreateScanData() => new(
        Catalog: SoftwareCatalog.Entries,
        InstalledWingetIds: new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            { "Git.Git", "Mozilla.Firefox" },
        InstalledStateKnown: true,
        WingetVersion: "v1.9.0-uitest",
        WindowsApps: WindowsAppsCatalog.Entries,
        PresentAppxPackageIds: WindowsAppsCatalog.Entries.Take(20).Select(e => e.PackageId)
            .ToHashSet(StringComparer.OrdinalIgnoreCase),
        AppxStateKnown: true,
        Upgradable:
        [
            new("Mozilla.Firefox", "Mozilla Firefox", "133.0", "134.0.1"),
            new("Git.Git", "Git", "2.47.0", "2.48.1"),
        ],
        UpgradableStateKnown: true);

    private static (UiSession Session, SoftwareViewModel ViewModel, PendingActionsService Queue) CreateSession()
    {
        var queue = new PendingActionsService();
        var viewModel = new SoftwareViewModel(CreateScanData(), queue);
        var session = UiSession.ForView(new SoftwareView(), viewModel, "software-view");
        return (session, viewModel, queue);
    }

    [AvaloniaFact]
    public void CatalogTab_RendersWithBadges()
    {
        var (session, _, _) = CreateSession();
        using (session)
        {
            session.Screenshot("catalog-initial");

            Assert.True(session.IsTextVisible("App Catalog"));
            Assert.True(session.IsTextVisible("Windows Apps"));
            // Fake install state marks Git/Firefox rows Installed.
            Assert.True(session.IsTextVisible("Installed"));
        }
    }

    [AvaloniaFact]
    public void ClickingInstall_StagesActionAndButtonReadsQueued()
    {
        var (session, _, queue) = CreateSession();
        using (session)
        {
            var installButton = session.Find<Avalonia.Controls.Button>(
                b => b.Content as string == "Install");
            session.Click(installButton);
            session.Screenshot("after-queue-click");

            Assert.Equal(1, queue.PendingCount);
            Assert.Equal("Queued", installButton.Content as string);

            // A second click takes it back out, like a person changing their mind.
            session.Click(installButton);
            Assert.Equal(0, queue.PendingCount);
            Assert.Equal("Install", installButton.Content as string);
        }
    }

    [AvaloniaFact]
    public void TypingInSearch_FiltersTheList()
    {
        var (session, viewModel, _) = CreateSession();
        using (session)
        {
            var searchBox = session.Find<Avalonia.Controls.TextBox>(t => t.Watermark == "Search apps");
            session.Type(searchBox, "firefox");
            session.Screenshot("search-firefox");

            Assert.True(viewModel.FilteredApps.Count is > 0 and < 10,
                $"Expected a filtered list, got {viewModel.FilteredApps.Count}");
            Assert.Contains(viewModel.FilteredApps, a => a.Name.Contains("Firefox", StringComparison.Ordinal));
        }
    }

    [AvaloniaFact]
    public void UpdatesTab_RendersRowsAndUpdateAllQueuesEverything()
    {
        var (session, viewModel, queue) = CreateSession();
        using (session)
        {
            session.ClickText("Updates");
            session.Screenshot("updates-tab");

            Assert.True(session.IsTextVisible("2 updates available."));
            Assert.True(session.IsTextVisible("133.0 to 134.0.1"));
            Assert.True(session.IsTextVisible("Update all"));

            session.ClickText("Update all");
            session.Screenshot("updates-all-queued");

            Assert.Equal(2, queue.PendingCount);
            Assert.All(viewModel.Updates, u => Assert.True(u.IsQueued));

            // A per-row click takes that one back out.
            var row = viewModel.Updates[0];
            row.ToggleQueueCommand.Execute(null);
            Assert.Equal(1, queue.PendingCount);
            Assert.False(row.IsQueued);
        }
    }

    [AvaloniaFact]
    public void WindowsAppsTab_RendersRemovalRows()
    {
        var (session, _, _) = CreateSession();
        using (session)
        {
            session.ClickText("Windows Apps");
            session.Screenshot("windows-apps-tab");

            Assert.True(session.IsTextVisible("Remove"));
        }
    }
}
