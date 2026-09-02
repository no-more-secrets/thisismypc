using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using ThisIsMyPC.App.ViewModels;
using ThisIsMyPC.App.Views;
using ThisIsMyPC.Core.Services;
using ThisIsMyPC.Modules.Startup.Changes;
using ThisIsMyPC.Modules.Startup.Models;
using ThisIsMyPC.Modules.Startup.Services;

namespace ThisIsMyPC.App.UiTests;

/// <summary>
/// The Startup &amp; Services page (the Autoruns inventory) with fake scan data:
/// a tab per category, a switch that stages a change, the Microsoft filter,
/// and the search mode that replaces the tabs with one list.
/// </summary>
public class AutorunsTabShotTests
{
    private static AutorunEntry Entry(AutorunCategory category, AutorunItemKind kind, string name, string location, string data,
        string? image = null, string? description = null, string? publisher = null, bool enabled = true, string? note = null) => new()
    {
        Category = category,
        Kind = kind,
        Name = name,
        Location = location,
        Data = data,
        ImagePath = image,
        Description = description,
        Publisher = publisher,
        IsEnabled = enabled,
        Note = note,
    };

    private static StartupScanData ScanData() => new([], [])
    {
        Autoruns =
        [
            Entry(AutorunCategory.Logon, AutorunItemKind.RegistryValue, "Acme Updater", StartupScanner.MachineRunKey,
                @"""C:\Program Files\Acme\updater.exe"" /tray", @"C:\Program Files\Acme\updater.exe", "Acme Update Agent", "Acme Inc."),
            Entry(AutorunCategory.Logon, AutorunItemKind.RegistryValue, "SecurityHealth", StartupScanner.MachineRunKey,
                @"%windir%\system32\SecurityHealthSystray.exe", @"C:\Windows\system32\SecurityHealthSystray.exe",
                "Windows Security notification icon", "Microsoft Corporation", note: "Off in Task Manager"),
            Entry(AutorunCategory.Logon, AutorunItemKind.StartupFile, "Old Tool.lnk",
                @"C:\ProgramData\Microsoft\Windows\Start Menu\Programs\Startup", @"C:\ProgramData\...\AutorunsDisabled\Old Tool.lnk",
                @"C:\Tools\old.exe", "Old Tool", "Acme Inc.", enabled: false),
            Entry(AutorunCategory.Logon, AutorunItemKind.RegistryValue, "Discord", StartupScanner.UserRunKey,
                @"C:\Users\me\AppData\Local\Discord\Update.exe --processStart Discord.exe", @"C:\Users\me\AppData\Local\Discord\Update.exe",
                "Discord", "Discord Inc.", note: "Re-registered itself after being switched off")
                with { LiveSnapshot = new AutorunSnapshot { Kind = AutorunItemKind.RegistryValue }.Serialize() },
            Entry(AutorunCategory.Explorer, AutorunItemKind.RegistryKey, "7-Zip", AutorunLocations.BackgroundContextMenuHandlersKey,
                "{23170F69-40C1-278A-1000-000100020000}", @"C:\Program Files\7-Zip\7-zip.dll", "7-Zip Shell Extension", "Igor Pavlov"),
            Entry(AutorunCategory.Services, AutorunItemKind.Service, "Spooler", AutorunLocations.ServicesKey,
                @"%SystemRoot%\System32\spoolsv.exe", @"C:\Windows\System32\spoolsv.exe", "Print Spooler", "Microsoft Corporation", note: "Automatic"),
            Entry(AutorunCategory.Drivers, AutorunItemKind.Service, "acmefilt", AutorunLocations.ServicesKey,
                @"System32\drivers\acmefilt.sys", @"C:\Windows\System32\drivers\acmefilt.sys", "Acme Filter Driver", "Acme Inc.", note: "Boot start"),
            Entry(AutorunCategory.ScheduledTasks, AutorunItemKind.ScheduledTask, "AcmeUpdateTask", @"\Acme\AcmeUpdateTask",
                @"\Acme\AcmeUpdateTask", description: "Checks for Acme updates", publisher: "Acme Inc."),
        ],
    };

    private static (StartupViewModel ViewModel, PendingChangesService Queue) Build()
    {
        var queue = new PendingChangesService();
        return (new StartupViewModel(ScanData(), queue), queue);
    }

    private static void ClickTab(UiSession session, string name)
        => session.Click(session.Find<TabItem>(t => t.DataContext is AutorunTabViewModel { Name: var n } && n == name));

    private static ToggleSwitch Switch(UiSession session, string name)
        => session.Find<ToggleSwitch>(t => t.DataContext is AutorunItemViewModel { Name: var n } && n == name);

    [AvaloniaFact]
    public void LogonTab_OpensFirstAndStagesATogglePendingChange()
    {
        var (viewModel, queue) = Build();
        using var session = UiSession.ForView(new StartupView(), viewModel, "autoruns-tab");

        session.Screenshot("logon");
        Assert.False(session.IsTextVisible("Everything"));
        Assert.True(session.IsTextVisible("Logon (4)"));
        Assert.True(session.IsTextVisible("Explorer (1)"));
        Assert.True(session.IsTextVisible("Off in Task Manager"));
        Assert.True(session.IsTextVisible("Re-registered itself after being switched off"));
        Assert.True(session.IsTextVisible("Old Tool.lnk"));
        Assert.False(session.IsTextVisible("acmefilt"));

        var acme = Switch(session, "Acme Updater");
        session.Click(acme);
        session.Screenshot("logon-acme-off-pending");

        var group = Assert.Single(queue.PendingGroups);
        var change = Assert.Single(group.Changes);
        Assert.Equal("Logon: Acme Updater", change.DisplayName);
        Assert.Equal("Disabled", change.AfterValue);
        Assert.Equal("RegistryValue|" + StartupScanner.MachineRunKey + "|Acme Updater", change.SystemLocation);
        Assert.StartsWith(AutorunChangeFactory.SettingIdPrefix, change.SettingId, StringComparison.Ordinal);
        Assert.True(session.IsTextVisible("Pending"));

        // Switching back on drops the staged group instead of stacking a second one.
        session.Click(acme);
        Assert.Empty(queue.PendingGroups);
    }

    [AvaloniaFact]
    public void CategoryTabs_AndTheMicrosoftFilter()
    {
        var (viewModel, _) = Build();
        using var session = UiSession.ForView(new StartupView(), viewModel, "autoruns-tab");

        ClickTab(session, "Drivers");
        session.Screenshot("drivers-tab");
        Assert.True(session.IsTextVisible("acmefilt"));
        Assert.True(session.IsTextVisible("Boot start"));
        Assert.False(session.IsTextVisible("Acme Updater"));

        ClickTab(session, "Font Drivers");
        Assert.True(session.IsTextVisible("Nothing in this category"));

        ClickTab(session, "Logon");
        session.ClickText("Hide Microsoft entries");
        session.Screenshot("logon-tab-hide-microsoft");
        Assert.True(viewModel.HideMicrosoftAutoruns);
        Assert.True(session.IsTextVisible("Logon (3 of 4)"));
        Assert.True(session.IsTextVisible("Services (0 of 1)"));
        Assert.False(session.IsTextVisible("SecurityHealth"));
    }

    [AvaloniaFact]
    public void Search_ReplacesTheTabsWithOneListAcrossEveryCategory()
    {
        var (viewModel, _) = Build();
        using var session = UiSession.ForView(new StartupView(), viewModel, "autoruns-tab");

        var box = session.Find<TextBox>(t => t.Classes.Contains("filter"));
        session.Type(box, "acme");
        session.Screenshot("search-acme");
        Assert.True(viewModel.IsSearching);
        Assert.False(session.IsTextVisible("Explorer (1)"));
        Assert.True(session.IsTextVisible("4 matches across every category"));
        Assert.True(session.IsTextVisible("Logon (2)"));
        Assert.True(session.IsTextVisible("Drivers (1)"));
        Assert.True(session.IsTextVisible("Scheduled Tasks (1)"));
        Assert.True(session.IsTextVisible("Acme Updater"));
        Assert.True(session.IsTextVisible("acmefilt"));
        Assert.False(session.IsTextVisible("7-Zip"));

        // A switch in the search list stages the same way.
        session.Click(Switch(session, "acmefilt"));
        Assert.True(session.IsTextVisible("Pending"));

        viewModel.AutorunFilterText = "zzz";
        session.Pump();
        Assert.True(session.IsTextVisible("Nothing matches"));

        viewModel.AutorunFilterText = string.Empty;
        session.Pump();
        session.Screenshot("search-cleared");
        Assert.False(viewModel.IsSearching);
        Assert.True(session.IsTextVisible("Explorer (1)"));
    }
}
