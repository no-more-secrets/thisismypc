using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Styling;
using Microsoft.Extensions.DependencyInjection;
using ThisIsMyPC.App.Services;
using ThisIsMyPC.App.ViewModels;
using ThisIsMyPC.App.Views;
using ThisIsMyPC.Core.Changes;
using ThisIsMyPC.Core.Modules;
using ThisIsMyPC.Core.Results;
using ThisIsMyPC.Core.Services;
using ThisIsMyPC.Modules.Startup.Models;

namespace ThisIsMyPC.App.UiTests;

public class GuideAutorunExampleTests
{
    [AvaloniaFact]
    public async Task RawEntriesGetPublishers_AndDiscardRestoresChoices()
    {
        var pending = new PendingChangesService();
        var raw = Example() with { Publisher = null };
        var vm = new GuideAutorunExampleViewModel(() => Task.FromResult(OperationResult<object>.Success(
            new StartupScanData([], [], Autoruns: [raw]))), pending, null, () => true,
            new AutorunEnrichment(signatures: new Signatures()));
        await vm.ShowAnotherCommand.ExecuteAsync(null);
        Assert.Equal("Example publisher (verified signer)", vm.Entry!.Publisher);
        await vm.DisableCommand.ExecuteAsync(null);
        Assert.True(vm.IsStaged);
        pending.DiscardAll();
        Assert.False(vm.IsStaged);
        Assert.True(vm.CanChoose);
        await vm.DisableCommand.ExecuteAsync(null);
        Assert.Single(pending.PendingGroups);
    }

    [AvaloniaFact]
    [Trait("Category", "Diagnostic")]
    public async Task RealGuideReadsLiveEntryWithoutStaging()
    {
        using var session = UiSession.ForMainWindow("guide-live-system");
        var main = (MainWindowViewModel)session.Window.DataContext!;
        await session.WaitForAsync(() => main.SidebarGroups.Count > 0, what: "sidebar");
        await main.OpenSetupGuideCommand.ExecuteAsync(null);
        var guide = (SetupGuideViewModel)main.CurrentContent!;
        guide.IsDesktop = true;
        guide.SelectedIndex = guide.Lessons.ToList().FindIndex(l => l.ModuleName == "Startup & Services");
        session.Pump();
        session.ClickText("Try live example");
        await session.WaitForAsync(() => !guide.AutorunExample!.IsBusy, what: "real startup scan");
        Assert.DoesNotContain("could not be read", guide.AutorunExample!.EmptyText, StringComparison.Ordinal);
        var module = session.Services!.GetServices<IModule>().Single(m => m.Info.Name == "Startup & Services");
        var scanned = await module.ScanSystemStateAsync();
        var data = Assert.IsType<StartupScanData>(scanned.Value);
        var signatures = session.Services.GetRequiredService<AutorunEnrichment>();
        var evidence = new List<string>();
        foreach (var entry in data.Autoruns.Where(e => e.Category == AutorunCategory.Logon))
        {
            var signature = entry.ImagePath is null ? null : await signatures.GetSignatureAsync(entry.ImagePath);
            evidence.Add($"{entry.Name}: {entry.Kind}, enabled={entry.IsEnabled}, exists={entry.FileExists}, note={entry.Note}, signature={signature}");
        }
        File.WriteAllLines(Path.Combine(session.ShotDirectory, "scan.txt"), evidence);
        session.Screenshot("real-entry");
        var scroll = session.Find<ScrollViewer>(s => s.Name == "DetailScroll");
        scroll.Offset = new(0, 320);
        session.Pump();
        session.Screenshot("real-entry-choices");
    }

    private sealed class Signatures : IAuthenticodeService
    {
        public SignatureInfo Check(string path) => new(SignatureState.Verified, "Example publisher");
    }

    [AvaloniaFact]
    public async Task DisabledSummaryCountsParkedLogonEntriesBeforeEnrichment_AndClearsOnFailure()
    {
        var fail = false;
        var disabled = Example("Disabled chat") with { IsEnabled = false, Publisher = null };
        var entries = new[] { Example(), disabled,
            disabled with { Name = "Disabled shortcut", Kind = AutorunItemKind.StartupFile },
            disabled with { Name = "Disabled task", Category = AutorunCategory.ScheduledTasks, Kind = AutorunItemKind.ScheduledTask },
            Example("Off in Task Manager") with { Note = "Off in Task Manager" },
            Example("Re-registered") with { LiveSnapshot = "snapshot" } };
        var vm = new GuideAutorunExampleViewModel(() => Task.FromResult(fail
            ? OperationResult<object>.Failure("Unavailable", ErrorCategory.ServiceUnavailable)
            : OperationResult<object>.Success(new StartupScanData([], [], Autoruns: entries))),
            new PendingChangesService(), null, () => true, new AutorunEnrichment(signatures: new Signatures()));
        await vm.ShowAnotherCommand.ExecuteAsync(null);
        Assert.Contains("2 sign-in entries", vm.DisabledSummary, StringComparison.Ordinal);
        Assert.True(vm.HasDisabledSummary);
        Assert.NotNull(vm.Entry);
        fail = true;
        await vm.DisableCommand.ExecuteAsync(null);
        Assert.Empty(vm.DisabledSummary);
        Assert.False(vm.HasDisabledSummary);
        fail = false;
        await vm.ShowAnotherCommand.ExecuteAsync(null);
        Assert.True(vm.HasDisabledSummary);
        fail = true;
        await vm.ShowAnotherCommand.ExecuteAsync(null);
        Assert.Empty(vm.DisabledSummary);
        Assert.False(vm.HasDisabledSummary);
    }

    [AvaloniaTheory]
    [InlineData(0, "No sign-in entries")]
    [InlineData(1, "1 sign-in entry disabled")]
    public async Task DisabledSummaryAppearsWithoutAnActiveExample(int count, string expected)
    {
        var entries = count == 0 ? Array.Empty<AutorunEntry>() : [Example() with { IsEnabled = false }];
        var vm = new GuideAutorunExampleViewModel(() => Task.FromResult(OperationResult<object>.Success(
            new StartupScanData([], [], Autoruns: entries))), new PendingChangesService(), null, () => true);
        await vm.ShowAnotherCommand.ExecuteAsync(null);
        Assert.Null(vm.Entry);
        Assert.Contains(expected, vm.DisabledSummary, StringComparison.Ordinal);
        Assert.True(vm.HasDisabledSummary);
    }

    private static AutorunEntry Example(string name = "Chat application") => new()
    {
        Name = name, Publisher = "Example publisher", ImagePath = @"C:\Program Files\Example\chat.exe",
        Data = "\"C:\\Program Files\\Example\\chat.exe\" --start-minimized",
        Location = @"HKEY_CURRENT_USER\Software\Microsoft\Windows\CurrentVersion\Run",
        Category = AutorunCategory.Logon, Kind = AutorunItemKind.RegistryValue, IsEnabled = true,
    };

    [AvaloniaFact]
    public async Task DisablingStagesOnly_AndCannotStageTwice()
    {
        var pending = new PendingChangesService();
        var scans = 0;
        var vm = new GuideAutorunExampleViewModel(() =>
        {
            scans++;
            return Task.FromResult(OperationResult<object>.Success(new StartupScanData([], [], Autoruns: [Example()])));
        }, pending, null, () => true);
        await vm.ShowAnotherCommand.ExecuteAsync(null);
        Assert.True(vm.CanChoose);
        await vm.DisableCommand.ExecuteAsync(null);
        Assert.Equal(2, scans);
        var change = Assert.Single(Assert.Single(pending.PendingGroups).Changes);
        Assert.Equal("Enabled", change.BeforeValue);
        Assert.Equal("Disabled", change.AfterValue);
        Assert.Equal(ChangeValueType.Autorun_State, change.ValueType);
        Assert.False(vm.CanChoose);
        await vm.DisableCommand.ExecuteAsync(null);
        Assert.Single(pending.PendingGroups);
        await vm.ShowAnotherCommand.ExecuteAsync(null);
        Assert.Null(vm.Entry);
        Assert.Contains("Sample:", vm.EmptyText, StringComparison.Ordinal);
    }

    [AvaloniaFact]
    public async Task ChangedEntryIsNotStaged_AndFailedScanCanRetry()
    {
        var pending = new PendingChangesService();
        var entry = Example();
        var fail = false;
        var vm = new GuideAutorunExampleViewModel(() => Task.FromResult(fail
            ? OperationResult<object>.Failure("Unavailable", ErrorCategory.ServiceUnavailable)
            : OperationResult<object>.Success(new StartupScanData([], [], Autoruns: [entry]))), pending, null, () => true);
        await vm.ShowAnotherCommand.ExecuteAsync(null);
        entry = entry with { Data = "different.exe" };
        await vm.DisableCommand.ExecuteAsync(null);
        Assert.Empty(pending.PendingGroups);
        Assert.Null(vm.Entry);
        fail = true;
        await vm.ShowAnotherCommand.ExecuteAsync(null);
        Assert.Null(vm.Entry);
        Assert.False(vm.IsBusy);
        fail = false;
        await vm.ShowAnotherCommand.ExecuteAsync(null);
        Assert.NotNull(vm.Entry);
    }

    [AvaloniaFact]
    public async Task KeepsAndCyclesEntries_ExcludesTasksAndAlreadyDisabledEntries()
    {
        var pending = new PendingChangesService();
        var entries = new[] { Example("One"), Example("Two"), Example("Task") with { Kind = AutorunItemKind.ScheduledTask },
            Example("Microsoft") with { Publisher = "Microsoft Corporation" }, Example("Off") with { Note = "Off in Task Manager" },
            Example("Disabled") with { IsEnabled = false } };
        var vm = new GuideAutorunExampleViewModel(() => Task.FromResult(OperationResult<object>.Success(
            new StartupScanData([], [], Autoruns: entries))), pending, null, () => true);
        await vm.ShowAnotherCommand.ExecuteAsync(null);
        var first = vm.Entry;
        Assert.Contains(first!.Name, new[] { "One", "Two" });
        await vm.KeepEnabledCommand.ExecuteAsync(null);
        Assert.NotEqual(first, vm.Entry);
        Assert.Empty(pending.PendingGroups);
    }

    [AvaloniaFact]
    public async Task UnsupportedDesktopDoesNotScan()
    {
        var vm = new GuideAutorunExampleViewModel(() => throw new InvalidOperationException("Must not scan"),
            new PendingChangesService(), null, () => false);
        await vm.ShowAnotherCommand.ExecuteAsync(null);
        Assert.Null(vm.Entry);
        Assert.False(vm.CanChoose);
    }

    [AvaloniaFact]
    public async Task GuideExampleRendersAndStagesThroughButtons()
    {
        var pending = new PendingChangesService();
        var guide = new SetupGuideViewModel([new(new Startup(), new(true))], null, _ => { }, _ => { }, () => { }, pending)
        { IsDesktop = true, SelectedIndex = 1 };
        using var session = UiSession.ForView(new SetupGuideView(), guide, "guide-live-example", width: 1000, height: 750);
        session.ClickText("Try live example");
        await session.WaitForAsync(() => guide.AutorunExample!.HasEntry, what: "live entry");
        foreach (var theme in new[] { ThemeVariant.Dark, ThemeVariant.Light })
        {
            session.SetTheme(theme);
            session.Screenshot($"{theme.Key}-top");
            var scroll = session.Find<ScrollViewer>(s => s.Name == "DetailScroll");
            scroll.Offset = new(0, 300);
            session.Pump();
            session.Screenshot($"{theme.Key}-choices");
            scroll.Offset = default;
        }
        var detail = session.Find<ScrollViewer>(s => s.Name == "DetailScroll");
        detail.Offset = new(0, 300);
        session.Pump();
        session.ClickText("Disable");
        await session.WaitForAsync(() => pending.PendingGroups.Count == 1, what: "staged example");
        Assert.False(guide.AutorunExample!.CanChoose);
        session.Screenshot("staged");
    }

    [AvaloniaFact]
    public async Task DisabledSummaryRendersInCompactEmptyExample()
    {
        var guide = new SetupGuideViewModel([new(new Startup([]), new(true))], null, _ => { }, _ => { }, () => { },
            new PendingChangesService()) { IsDesktop = true, SelectedIndex = 1 };
        using var session = UiSession.ForView(new SetupGuideView(), guide, "guide-disabled-empty", width: 800, height: 600);
        session.ClickText("Try live example");
        await session.WaitForAsync(() => guide.AutorunExample!.HasDisabledSummary, what: "disabled count");
        Assert.False(guide.AutorunExample!.HasEntry);
        Assert.Contains("No sign-in entries are already disabled", session.DescribeVisibleText(), StringComparison.Ordinal);
        session.Screenshot("empty-count");
    }

    private sealed class Startup(AutorunEntry[]? entries = null) : IModule
    {
        public ModuleInfo Info { get; } = new("Startup & Services", "startup", "Startup", [], ModuleGroup.Core, 4);
        public Task<ModuleAvailability> CheckAvailabilityAsync() => Task.FromResult(new ModuleAvailability(true));
        public Task<OperationResult<object>> ScanSystemStateAsync() => Task.FromResult(OperationResult<object>.Success(
            new StartupScanData([], [], Autoruns: entries ?? [Example(), Example("Previously disabled") with { IsEnabled = false },
                Example("Another disabled entry") with { IsEnabled = false }])));
        public Task<OperationResult<bool>> ApplyChangeAsync(ChangeDescriptor change) => throw new InvalidOperationException("Must not apply");
        public Task<OperationResult<bool>> RevertChangeAsync(ChangeDescriptor change) => throw new InvalidOperationException("Must not revert");
    }
}
