using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Styling;
using Avalonia.VisualTree;
using ThisIsMyPC.App.ViewModels;
using ThisIsMyPC.App.Views;
using ThisIsMyPC.Core.Services;
using ThisIsMyPC.Core.Sets;
using ThisIsMyPC.Core.Changes;
using ThisIsMyPC.Modules.Startup.Models;

namespace ThisIsMyPC.App.UiTests;

/// <summary>
/// The Presets page with fake sets: cards wear the shared clickable states
/// (hover wash, active tint with the accent ring when chosen), and the list
/// keeps the standard 16px edge beside the overlay scrollbar so the thumb
/// never sits on a card. Fake inspectors exercise staging without system writes.
/// </summary>
public class SetLoaderViewShotTests
{
    private const double ContentEdge = 16;

    private static SetDefinition Definition(string name, SetCategory category, int entries, string description) => new()
    {
        Name = name,
        Description = description,
        Category = category,
        Version = "1.0.0",
        Author = "ThisIsMyPC",
        Source = SetSource.BuiltIn,
        FilePath = $@"C:\sets\{name}.json",
        Entries = Enumerable.Range(1, entries).Select(i => new SetEntry
        {
            ModuleId = "Windows Annoyances",
            SettingId = $"setting-{i}",
            Value = "0",
            Description = $"Change {i} of {name}",
        }).ToList(),
    };

    private static SetLoaderViewModel Build() => new(
        new SetLoadResult
        {
            Sets =
            [
                Definition("Clean Boot", SetCategory.TweakSet, 32,
                    "Disables the telemetry, diagnostics, and unused-feature services and scheduled tasks that are safe to turn off on any Windows 11 machine."),
                Definition("NukeCopilot", SetCategory.TweakSet, 3,
                    "Turns off Windows Copilot everywhere it surfaces: the assistant itself, the taskbar button, and Edge's Copilot sidebar."),
                Definition("Privacy Baseline", SetCategory.TweakSet, 15,
                    "Limits diagnostic data, disables error reporting, the advertising ID, activity history, Windows Recall, and the ad-like suggestion surfaces."),
                Definition("Quiet Desktop", SetCategory.TweakSet, 9,
                    "Removes widgets, news, and the search highlights from the taskbar and lock screen."),
                Definition("Everything", SetCategory.OptimizationPack, 40,
                    "Every tweak set above in one bundle, grouped by the set each change came from."),
            ],
            Warnings = [],
        },
        [],
        _ => null,
        new PendingChangesService());

    private static Border Card(UiSession session, string name)
        => session.Find<Border>(b => b.Classes.Contains("set-card-body") && b.DataContext is SetItemViewModel { Name: var n } && n == name);

    /// <summary>The control's right edge in window pixels.</summary>
    private static double RightOf(UiSession session, Control control)
        => (control.TranslatePoint(new Avalonia.Point(control.Bounds.Width, 0), session.Window)
            ?? throw new InvalidOperationException("Control is not connected to the window's visual tree.")).X;

    [AvaloniaFact]
    public void Cards_HoverAndSelectLikeEveryOtherControl()
    {
        var viewModel = Build();
        using var session = UiSession.ForView(new SetLoaderView(), viewModel, "set-loader");

        session.Screenshot("rest");
        Assert.True(session.IsTextVisible("Tweaks"));
        Assert.True(session.IsTextVisible("Packs"));

        session.Hover(Card(session, "NukeCopilot"));
        session.Screenshot("nukecopilot-hovered");
        Assert.Null(viewModel.SelectedSet);

        session.Click(Card(session, "Clean Boot"));
        session.Screenshot("clean-boot-selected");
        var cleanBoot = viewModel.TweakSets.Single(s => s.Name == "Clean Boot");
        Assert.Same(cleanBoot, viewModel.SelectedSet);
        Assert.True(cleanBoot.IsSelected);
        Assert.True(Card(session, "Clean Boot").Classes.Contains("selected"));
        Assert.False(Card(session, "NukeCopilot").Classes.Contains("selected"));
        Assert.True(session.IsTextVisible("setting-1"));

        // Selecting another card moves the tint; the pointer resting on it does not remove it.
        session.Click(Card(session, "NukeCopilot"));
        session.Screenshot("nukecopilot-selected-hovered");
        Assert.False(cleanBoot.IsSelected);
        Assert.True(viewModel.TweakSets.Single(s => s.Name == "NukeCopilot").IsSelected);
        Assert.True(Card(session, "NukeCopilot").Classes.Contains("selected"));
    }

    [AvaloniaFact]
    public void List_KeepsTheContentEdgeBesideTheScrollbar()
    {
        using var session = UiSession.ForView(new SetLoaderView(), Build(), "set-loader", height: 480);

        var card = Card(session, "Clean Boot");
        var scroller = card.FindAncestorOfType<ScrollViewer>()!;
        Assert.Equal(ContentEdge, RightOf(session, scroller) - RightOf(session, card), 0.5);
        // Enough cards to scroll, so the screenshot shows the thumb beside the cards.
        Assert.True(scroller.Extent.Height > scroller.Viewport.Height);
        session.Screenshot("list-edge");
    }

    [AvaloniaFact]
    public void Cards_RenderInLightTheme()
    {
        var viewModel = Build();
        using var session = UiSession.ForView(new SetLoaderView(), viewModel, "set-loader");
        try
        {
            session.SetTheme(ThemeVariant.Light);
            session.Click(Card(session, "Privacy Baseline"));
            session.Hover(Card(session, "Clean Boot"));
            session.Screenshot("light-selected-and-hovered");
            Assert.True(viewModel.TweakSets.Single(s => s.Name == "Privacy Baseline").IsSelected);
        }
        finally
        {
            session.SetTheme(ThemeVariant.Dark);
        }
    }

    [AvaloniaFact]
    public void Tabs_Search_Links_AndStaging_KeepTheirTargets()
    {
        var pending = new PendingChangesService();
        (string Module, string Setting, string Name)? destination = null;
        using var vm = new SetLoaderViewModel(new SetLoadResult
        {
            Sets = [Definition("Desktop", SetCategory.TweakSet, 12, "Desktop choices"),
                Definition("Complete", SetCategory.OptimizationPack, 12, "All desktop choices")],
            Warnings = [],
        }, [new PreviewInspector()], _ => new(true), pending,
            navigateToSetting: (module, setting, name) => destination = (module, setting, name));
        using var session = UiSession.ForView(new SetLoaderView(), vm, "set-loader", width: 900);
        session.ClickText("Packs");
        Assert.Single(vm.CurrentSets);
        Assert.Equal("Complete", vm.CurrentSets[0].Name);
        session.Click(Card(session, "Complete"));
        session.Screenshot("pack-compact");
        var rows = vm.PreviewGroups.SelectMany(g => g.Entries).ToList();
        Assert.True(rows[0].IsApplied);
        Assert.Equal(11, vm.IncludedCount);
        var search = session.Find<TextBox>(b => b.Name == "PresetSearch");
        session.Type(search, "setting-1");
        Assert.Equal(4, vm.VisibleRows.OfType<SetEntryPreviewViewModel>().Count());
        session.ClickText("Desktop setting 1");
        Assert.Equal(("Windows Annoyances", "setting-1", "Desktop setting 1"), destination);
        rows[1].IsIncluded = false;
        session.ClickText("Stage 10 changes");
        Assert.Equal(10, pending.PendingGroups.Count);
        Assert.DoesNotContain(pending.PendingGroups, g => g.GroupId == "setting-2");
        Assert.Contains(pending.PendingGroups, g => g.GroupId == "setting-3");
        search.Text = "no matching entry";
        session.Pump();
        Assert.True(session.IsTextVisible("No matching changes"));
        session.Screenshot("search-empty");
        search.Text = string.Empty;
        session.SetTheme(ThemeVariant.Light);
        session.Screenshot("pack-light");
        session.ClickText("Tweaks");
        Assert.Null(vm.SelectedSet);
        Assert.Equal("Desktop", vm.CurrentSets.Single().Name);
    }

    private sealed class PreviewInspector : ISetEntryInspector
    {
        public string ModuleId => "Windows Annoyances";
        public SetEntryState Inspect(SetEntry entry) => new()
        {
            SettingDisplayName = $"Desktop setting {entry.SettingId[8..]}",
            CurrentValue = entry.SettingId == "setting-1" ? "0" : "1",
            CurrentDisplay = entry.SettingId == "setting-1" ? "Off" : "On",
            IsApplied = entry.SettingId == "setting-1",
        };
        public ChangeGroup CreateChangeGroup(SetEntry entry) => new()
        {
            GroupId = entry.SettingId, DisplayName = entry.SettingId, Description = entry.Description,
            Changes = [new ChangeDescriptor
            {
                ModuleId = ModuleId, SettingId = entry.SettingId, DisplayName = entry.SettingId,
                SystemLocation = "fake", BeforeValue = "1", AfterValue = "0",
                BeforeDisplay = "On", AfterDisplay = "Off", ValueType = ChangeValueType.Registry_String,
            }],
        };
    }

    [AvaloniaFact]
    public void StartupLinks_RevealHiddenServicesAndTasks()
    {
        using var vm = new StartupViewModel(new StartupScanData([], [], Autoruns:
        [
            new AutorunEntry { Category = AutorunCategory.Services, Kind = AutorunItemKind.Service,
                Name = "DiagTrack", Location = @"HKLM\SYSTEM\CurrentControlSet\Services", Data = "",
                Publisher = "Microsoft Windows", IsEnabled = true },
            new AutorunEntry { Category = AutorunCategory.ScheduledTasks, Kind = AutorunItemKind.ScheduledTask,
                Name = "Consolidator", Location = @"\Microsoft\Windows\Customer Experience Improvement Program\Consolidator",
                Data = "", Publisher = "Microsoft Windows", IsEnabled = true },
            new AutorunEntry { Category = AutorunCategory.Logon, Kind = AutorunItemKind.RegistryValue,
                Name = "Example:Launcher", Location = @"HKCU\Software\Microsoft\Windows\CurrentVersion\Run",
                Data = "", IsEnabled = true },
        ]), new PendingChangesService());
        using var session = UiSession.ForView(new StartupView(), vm, "set-loader-startup");
        vm.NavigateToSearchResult("service-starttype:DiagTrack", "Connected User Experiences");
        session.Pump();
        Assert.True(vm.ShowWindowsEntries);
        Assert.True(vm.ShowMicrosoftEntries);
        Assert.Equal("DiagTrack", Assert.Single(vm.SearchResults.OfType<AutorunItemViewModel>()).Name);
        vm.NavigateToSearchResult(@"scheduled-task:\Microsoft\Windows\Customer Experience Improvement Program\Consolidator", "Consolidator");
        session.Pump();
        Assert.Equal("Consolidator", Assert.Single(vm.SearchResults.OfType<AutorunItemViewModel>()).Name);
        session.Screenshot("preset-task-destination");
        vm.NavigateToSearchResult("startup-entry:CurrentUserRun:Example:Launcher", "Example launcher");
        session.Pump();
        Assert.Equal("Example:Launcher", Assert.Single(vm.SearchResults.OfType<AutorunItemViewModel>()).Name);
    }

    [AvaloniaFact(Timeout = 300_000)]
    [Trait("Category", "Diagnostic")]
    public async Task MainWindow_Presets_OpensTheCorrespondingSetting()
    {
        using var session = UiSession.ForMainWindow("presets-main");
        session.Window.Width = 1200;
        var main = (MainWindowViewModel)session.Window.DataContext!;
        await session.WaitForAsync(() => main.SidebarGroups.Count > 0, timeoutMs: 30_000, what: "sidebar");
        session.ClickText("Presets");
        await session.WaitForAsync(() => main.CurrentContent is SetLoaderViewModel, what: "Presets");
        var vm = (SetLoaderViewModel)main.CurrentContent!;
        var preset = vm.OptimizationPacks.First(s => s.Definition.Entries.Any(e => e.ModuleId == "Explorer"));
        session.ClickText("Packs");
        session.ClickText(preset.Name);
        var card = session.Find<Border>(b => b.Name == "ModuleContentHost");
        Assert.Equal(default, card.Padding);
        var browser = session.Find<Border>(b => b.Name == "PresetBrowser");
        Assert.Equal(0, browser.TranslatePoint(default, card)!.Value.X, 0.5);
        Assert.Equal(default, card.BorderThickness);
        Assert.Equal(Avalonia.Media.Colors.Transparent, ((Avalonia.Media.ISolidColorBrush)card.Background!).Color);
        var details = session.Find<Border>(b => b.Name == "PresetDetails");
        var strip = session.Find<Border>(b => b.Name == "PART_Strip");
        Assert.Equal(browser.Bounds.Width - 2, strip.Bounds.Width, 0.5);
        Assert.Equal(session.TopOf(browser), session.TopOf(details), 0.5);
        Assert.Equal(16, details.TranslatePoint(default, card)!.Value.X - browser.Bounds.Width, 0.5);
        Assert.False(details.GetVisualDescendants().OfType<TabControl>().Any());
        var screenshot = session.Screenshot("dark-1200");
        using (var pixels = SkiaSharp.SKBitmap.Decode(screenshot))
        {
            var gapX = (int)(browser.TranslatePoint(default, session.Window)!.Value.X + browser.Bounds.Width + 8);
            var gapY = (int)(session.TopOf(browser) + browser.Bounds.Height / 2);
            Assert.Equal(new SkiaSharp.SKColor(26, 26, 46), pixels.GetPixel(gapX, gapY));
        }
        session.SetTheme(ThemeVariant.Light);
        session.Screenshot("light-1200");
        session.SetTheme(ThemeVariant.Dark);
        var row = vm.PreviewGroups.SelectMany(g => g.Entries).First(r => r.Entry.ModuleId == "Explorer" && !r.IsSkipped);
        session.Type(session.Find<TextBox>(b => b.Name == "PresetSearch"), row.Entry.SettingId);
        session.ClickText(row.SettingName);
        await session.WaitForAsync(() => main.CurrentContent is ShellViewModel { SearchText.Length: > 0 },
            timeoutMs: 120_000, what: "Explorer setting destination");
        Assert.False(main.UsesSeparateContentCards);
        Assert.Equal(new Thickness(1), card.BorderThickness);
        Assert.Equal(default, card.Padding);
        session.Screenshot("explorer-destination");
    }
}
