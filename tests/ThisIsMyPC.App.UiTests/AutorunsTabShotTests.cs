using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using ThisIsMyPC.App.Services;
using ThisIsMyPC.App.UiTests.Fakes;
using ThisIsMyPC.App.ViewModels;
using ThisIsMyPC.App.Views;
using ThisIsMyPC.Core.Services;
using ThisIsMyPC.Modules.Startup.Changes;
using ThisIsMyPC.Modules.Startup.Models;
using ThisIsMyPC.Modules.Startup.Services;

namespace ThisIsMyPC.App.UiTests;

/// <summary>
/// The Startup &amp; Services page (the Autoruns inventory) with fake scan data:
/// a tab per category with location headers, icons and signers arriving after
/// load, Autoruns' row colors, a switch that stages a change, the Windows and
/// Microsoft filters, and the search mode that replaces the tabs with one list.
/// </summary>
public class AutorunsTabShotTests
{
    private static readonly DateTime Stamp = new(2026, 8, 3, 16, 46, 16);

    private static AutorunEntry Entry(AutorunCategory category, AutorunItemKind kind, string name, string location, string data,
        string? image = null, string? description = null, string? publisher = null, bool enabled = true, string? note = null,
        bool exists = true) => new()
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
        FileExists = exists,
        Timestamp = exists && image is not null ? Stamp : null,
        LocationTimestamp = Stamp.AddDays(30),
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
            Entry(AutorunCategory.Logon, AutorunItemKind.RegistryValue, "Twinkle Tray", StartupScanner.UserRunKey,
                @"C:\Users\me\AppData\Local\Programs\twinkle-tray\Twinkle Tray.exe", @"C:\Users\me\AppData\Local\Programs\twinkle-tray\Twinkle Tray.exe",
                "Twinkle Tray", "Xander Frangos"),
            Entry(AutorunCategory.Logon, AutorunItemKind.RegistryValue, "com.squirrel.splice.Splice", StartupScanner.UserRunKey,
                @"C:\Users\me\AppData\Local\splice\app-4.2.77773\Splice.exe", @"C:\Users\me\AppData\Local\splice\app-4.2.77773\Splice.exe",
                exists: false),
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
                @"""C:\Program Files\Acme\updater.exe"" /check", @"C:\Program Files\Acme\updater.exe", "Checks for Acme updates", "Acme Inc."),
            Entry(AutorunCategory.ScheduledTasks, AutorunItemKind.ScheduledTask, "ScheduledDefrag", @"\Microsoft\Windows\Defrag\ScheduledDefrag",
                @"%windir%\system32\defrag.exe -c", @"C:\Windows\system32\defrag.exe", "This task optimizes local storage drives.", "Microsoft Corporation"),
            Entry(AutorunCategory.ScheduledTasks, AutorunItemKind.ScheduledTask, "PerformRemediation", @"\Microsoft\Windows\WaaSMedic\PerformRemediation",
                "{72566E27-1ABB-4EB3-B4F0-EB431CB1CB32}", publisher: "Microsoft Corporation"),
        ],
    };

    private static (StartupViewModel ViewModel, PendingChangesService Queue) Build()
    {
        var queue = new PendingChangesService();
        var enrichment = new AutorunEnrichment(new UiFakeFileIconService(), new UiFakeAuthenticodeService());
        return (new StartupViewModel(ScanData(), queue, enrichment), queue);
    }

    private static void ClickTab(UiSession session, string name)
        => session.Click(session.Find<TabItem>(t => t.DataContext is AutorunTabViewModel { Name: var n } && n == name));

    private static CheckBox Check(UiSession session, string name)
        => session.Find<CheckBox>(c => c.DataContext is AutorunItemViewModel { Name: var n } && n == name);

    private static Task WaitForSignersAsync(UiSession session, StartupViewModel viewModel)
        => session.WaitForAsync(() => !viewModel.IsCheckingSignatures, what: "icons and signers to load");

    [AvaloniaFact]
    public async Task LogonTab_ShowsLocationsIconsSignersAndColorsAndStagesAToggle()
    {
        var (viewModel, queue) = Build();
        using var session = UiSession.ForView(new StartupView(), viewModel, "autoruns-tab");
        await WaitForSignersAsync(session, viewModel);

        session.Screenshot("logon");
        // Windows and Microsoft entries are hidden until ticked: SecurityHealth is Windows.
        Assert.True(session.IsTextVisible("Logon (5 of 6)"));
        Assert.False(session.IsTextVisible("SecurityHealth"));
        // Dense by default: no location headers, no paths, until asked for.
        Assert.False(session.IsTextVisible(StartupScanner.MachineRunKey));
        Assert.False(session.IsTextVisible(@"File not found: C:\Users\me\AppData\Local\splice\app-4.2.77773\Splice.exe"));
        session.ClickText("Paths");
        session.ClickText("Locations");
        session.Screenshot("logon-paths-and-locations");
        Assert.True(session.IsTextVisible(StartupScanner.MachineRunKey));
        Assert.True(session.IsTextVisible(StartupScanner.UserRunKey));
        Assert.True(session.IsTextVisible("(Verified) Acme Inc."));
        Assert.True(session.IsTextVisible("(Not verified) Xander Frangos"));
        Assert.True(session.IsTextVisible(@"File not found: C:\Users\me\AppData\Local\splice\app-4.2.77773\Splice.exe"));
        Assert.True(session.IsTextVisible("Re-registered itself after being switched off"));
        Assert.True(session.IsTextVisible(AutorunItemViewModel.FormatTimestamp(Stamp)));

        var acmeRow = viewModel.Tabs[0].Items.OfType<AutorunItemViewModel>().First(r => r.Name == "Acme Updater");
        Assert.NotNull(acmeRow.Icon);
        Assert.True(viewModel.Tabs[0].Items.OfType<AutorunItemViewModel>().First(r => r.Name == "Twinkle Tray").IsUnverified);
        Assert.True(viewModel.Tabs[0].Items.OfType<AutorunItemViewModel>().First(r => r.Name == "com.squirrel.splice.Splice").IsMissing);

        var acme = Check(session, "Acme Updater");
        session.Click(acme);
        session.Screenshot("logon-acme-off-pending");

        var group = Assert.Single(queue.PendingGroups);
        var change = Assert.Single(group.Changes);
        Assert.Equal("Logon: Acme Updater", change.DisplayName);
        Assert.Equal("Disabled", change.AfterValue);
        Assert.Equal("RegistryValue|" + StartupScanner.MachineRunKey + "|Acme Updater", change.SystemLocation);
        Assert.StartsWith(AutorunChangeFactory.SettingIdPrefix, change.SettingId, StringComparison.Ordinal);
        Assert.True(session.IsTextVisible("Pending"));

        session.Click(acme);
        Assert.Empty(queue.PendingGroups);

        // A queued flip must read on a row that is already red: the bar at the left.
        var twinkle = Check(session, "Twinkle Tray");
        session.Click(twinkle);
        session.Screenshot("logon-unverified-queued");
        Assert.Single(queue.PendingGroups);
        session.Click(twinkle);
        Assert.Empty(queue.PendingGroups);
    }

    [AvaloniaFact]
    public async Task WindowsAndMicrosoftFilters_AndCategoryTabs()
    {
        var (viewModel, _) = Build();
        using var session = UiSession.ForView(new StartupView(), viewModel, "autoruns-tab");
        await WaitForSignersAsync(session, viewModel);

        Assert.True(session.IsTextVisible("Services (0 of 1)"));
        Assert.True(session.IsTextVisible("Scheduled Tasks (1 of 3)"));
        // SecurityHealth is both Windows and Microsoft, so both boxes must be on to see it.
        session.ClickText("Windows");
        Assert.True(viewModel.ShowWindowsEntries);
        Assert.False(session.IsTextVisible("SecurityHealth"));
        session.ClickText("Microsoft");
        Assert.True(viewModel.ShowMicrosoftEntries);
        Assert.True(session.IsTextVisible("Logon (6)"));
        Assert.True(session.IsTextVisible("SecurityHealth"));
        Assert.True(session.IsTextVisible("Off in Task Manager"));

        ClickTab(session, "Services");
        session.Screenshot("services-tab-windows-shown");
        Assert.True(session.IsTextVisible("Spooler"));
        Assert.True(session.IsTextVisible("(Verified) Microsoft Windows"));

        ClickTab(session, "Drivers");
        session.Screenshot("drivers-tab");
        Assert.True(session.IsTextVisible("acmefilt"));
        Assert.True(session.IsTextVisible("Boot start"));

        ClickTab(session, "Font Drivers");
        Assert.True(session.IsTextVisible("Nothing in this category"));

        // Every task sits under one header; a task's own path is not a location.
        viewModel.ShowLocations = true;
        viewModel.ShowPaths = true;
        session.Pump();
        ClickTab(session, "Scheduled Tasks");
        session.Screenshot("scheduled-tasks-windows-shown");
        Assert.True(session.IsTextVisible(AutorunEntry.TaskSchedulerLocation));
        Assert.True(session.IsTextVisible("ScheduledDefrag"));
        Assert.True(session.IsTextVisible(@"C:\Windows\system32\defrag.exe"));
        Assert.True(session.IsTextVisible("PerformRemediation"));
        var taskTab = viewModel.Tabs.First(t => t.Name == "Scheduled Tasks");
        Assert.Single(taskTab.Items.OfType<AutorunLocationHeader>());

        ClickTab(session, "Logon");
        session.ClickText("Microsoft");
        session.Screenshot("logon-tab-hide-microsoft");
        Assert.False(viewModel.ShowMicrosoftEntries);
        Assert.True(session.IsTextVisible("Logon (5 of 6)"));
        Assert.False(session.IsTextVisible("SecurityHealth"));
    }

    [AvaloniaFact]
    public async Task Search_ReplacesTheTabsWithOneListAcrossEveryCategory()
    {
        var (viewModel, _) = Build();
        using var session = UiSession.ForView(new StartupView(), viewModel, "autoruns-tab");
        await WaitForSignersAsync(session, viewModel);

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

        session.Click(Check(session, "acmefilt"));
        Assert.True(session.IsTextVisible("Pending"));

        viewModel.AutorunFilterText = "zzz";
        session.Pump();
        Assert.True(session.IsTextVisible("Nothing matches"));

        viewModel.AutorunFilterText = string.Empty;
        session.Pump();
        Assert.False(viewModel.IsSearching);
        Assert.True(session.IsTextVisible("Explorer (1)"));
    }
}
