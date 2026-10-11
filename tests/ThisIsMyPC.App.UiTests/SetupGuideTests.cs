using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Styling;
using Avalonia.VisualTree;
using Microsoft.Extensions.DependencyInjection;
using ThisIsMyPC.App.Services;
using ThisIsMyPC.App.ViewModels;
using ThisIsMyPC.App.Views;
using ThisIsMyPC.Core.Changes;
using ThisIsMyPC.Core.Hardware;
using ThisIsMyPC.Core.Modules;
using ThisIsMyPC.Core.Packages;
using ThisIsMyPC.Core.Results;
using ThisIsMyPC.Core.Services;
using ThisIsMyPC.Core.Sets;

namespace ThisIsMyPC.App.UiTests;

public class SetupGuideTests
{
    [AvaloniaFact]
    [Trait("Category", "Diagnostic")]
    public async Task MainWindow_LongLessonsRemainReadableAndScrollToActions()
    {
        using var session = UiSession.ForMainWindow("setup-guide-long-lessons", services =>
            services.AddSingleton<IHardwareDetectionService>(new Hardware(3)));
        var main = (MainWindowViewModel)session.Window.DataContext!;
        await session.WaitForAsync(() => main.SidebarGroups.Count > 0, what: "sidebar");
        session.ClickText("Intro guide");
        await session.WaitForAsync(() => main.CurrentContent is SetupGuideViewModel { IsDesktop: true }, what: "guide");
        var guide = (SetupGuideViewModel)main.CurrentContent!;
        var measurements = new List<string>();
        foreach (var height in new[] { 800, 700 })
        {
            session.Window.Height = height;
            foreach (var name in new[] { "Software", "Startup & Services" })
            {
                var lesson = guide.Lessons.Single(l => l.ModuleName == name);
                session.Click(session.Find<Button>(b => ReferenceEquals(b.DataContext, lesson)));
                session.Pump();
                var scroll = session.Find<ScrollViewer>(s => s.Name == "DetailScroll");
                Assert.Equal(0, scroll.Offset.Y);
                Assert.True(scroll.Extent.Height > scroll.Viewport.Height);
                measurements.Add($"{name}: {session.Window.Width}x{height}, {lesson.Body.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries).Length} words, content {scroll.Extent.Height:F0}px, viewport {scroll.Viewport.Height:F0}px, {scroll.Extent.Height / scroll.Viewport.Height:F2} screens");
                var label = name == "Software" ? "software" : "startup";
                session.Screenshot($"{label}-{height}-top");
                var point = scroll.TranslatePoint(new Point(scroll.Bounds.Width / 2, scroll.Bounds.Height / 2), session.Window)!.Value;
                for (var page = 1; scroll.Offset.Y + scroll.Viewport.Height < scroll.Extent.Height - 1; page++)
                {
                    Assert.True(page < 20, "Lesson did not reach its end.");
                    var before = scroll.Offset.Y;
                    session.Window.MouseWheel(point, new Vector(0, -8));
                    session.Pump();
                    Assert.True(scroll.Offset.Y > before);
                    session.Screenshot($"{label}-{height}-scroll-{page}");
                }
                var action = session.Find<Button>(b => Equals(b.Content, "Open module"));
                var origin = action.TranslatePoint(default, scroll)!.Value;
                Assert.InRange(origin.Y, 0, scroll.Viewport.Height - action.Bounds.Height);
            }
        }
        File.WriteAllLines(Path.Combine(session.ShotDirectory, "measurements.txt"), measurements);
    }

    [AvaloniaFact]
    public void OptionalPackEntry_RequiresSelectionBeforeStaging()
    {
        var row = new SetEntryPreviewViewModel(new()
        {
            Entry = new() { ModuleId = "Startup & Services", SettingId = "service-starttype:WSearch",
                Value = "Disabled", Description = "Disable indexing", SelectedByDefault = false },
            State = new() { SettingDisplayName = "Windows Search", CurrentValue = "Automatic", CurrentDisplay = "Automatic", IsApplied = false },
            SkipReason = null, Conflict = SetEntryConflict.None,
        }, () => { });
        Assert.False(row.IsIncluded);
        Assert.True(row.CanToggle);
        row.IsIncluded = true;
        Assert.True(row.IsIncluded);
    }

    [AvaloniaFact]
    public void StricterPolicy_IsShownAsCoveredAndNotSelected()
    {
        var entry = new SetEntry { ModuleId = "Privacy & Telemetry", SettingId = "telemetry-level", Value = "1", Description = "Required diagnostics" };
        var row = new SetEntryPreviewViewModel(new()
        {
            Entry = entry, Conflict = SetEntryConflict.None, SkipReason = null,
            State = new() { SettingDisplayName = "Diagnostic data", CurrentValue = "0", CurrentDisplay = "Off", IsApplied = false, CoveredByPolicy = "A stricter policy is already set." },
        }, () => { });
        Assert.True(row.IsApplied);
        Assert.Equal("Already covered", row.AppliedLabel);
        Assert.False(row.IsIncluded);
        Assert.False(row.CanToggle);
        Assert.Equal("Current: Off", row.ValuesSummary);
    }

    [AvaloniaFact]
    [Trait("Category", "Diagnostic")]
    public async Task MainWindow_GuideReturnsToSameModule_AndOpensBundledPack()
    {
        using var session = UiSession.ForMainWindow("setup-guide-window", services =>
        {
            services.AddSingleton<IHardwareDetectionService>(new Hardware(3));
            services.AddSingleton<IAppxPackageService>(new Packages());
        });
        var main = (MainWindowViewModel)session.Window.DataContext!;
        await session.WaitForAsync(() => main.SidebarGroups.Count > 0, what: "sidebar");
        session.Screenshot("home");
        session.OpenModule("Windows Annoyances");
        await session.WaitForAsync(() => !main.IsModuleLoading && main.CurrentContent is SettingCardPageViewModel, what: "annoyances");
        Assert.False(main.CanReturnToGuide);
        session.ClickText("Home");
        session.ClickText("Intro guide");
        await session.WaitForAsync(() => main.CurrentContent is SetupGuideViewModel { IsDesktop: true }, what: "guide");
        var guide = (SetupGuideViewModel)main.CurrentContent!;
        guide.SelectedIndex = guide.Lessons.ToList().FindIndex(l => l.ModuleName == "Windows Annoyances");
        session.Pump();
        session.Screenshot("guide");
        session.Window.Height = 700;
        session.Pump();
        session.Screenshot("guide-compact");
        var selectedCard = session.Find<Border>(b => b.Classes.Contains("set-card-body") && ReferenceEquals(b.DataContext, guide.Current));
        var lessonScroll = selectedCard.FindAncestorOfType<ScrollViewer>()!;
        Assert.InRange(selectedCard.TranslatePoint(default, lessonScroll)!.Value.Y,
            -0.5, lessonScroll.Bounds.Height - selectedCard.Bounds.Height + 0.5);
        session.Window.Height = 800;
        session.Pump();
        session.ClickText("Include in pack");
        Assert.False(guide.Current.IncludeRecommendations);
        session.ClickText("Open module");
        await session.WaitForAsync(() => !main.IsModuleLoading && main.CurrentContent is SettingCardPageViewModel, what: "same module");
        Assert.True(main.CanReturnToGuide);
        session.Screenshot("module-return-to-guide");
        session.ClickText("Return to guide");
        await session.WaitForAsync(() => main.CurrentContent is SetupGuideViewModel, what: "resume");
        Assert.Same(guide, main.CurrentContent);
        Assert.Equal("Windows Annoyances", guide.Current.ModuleName);
        Assert.False(guide.Current.IncludeRecommendations);
        Assert.False(main.CanReturnToGuide);
        session.ClickText("Open module");
        await session.WaitForAsync(() => !main.IsModuleLoading && main.CanReturnToGuide, what: "module revisit");
        session.OpenModule("Explorer");
        await session.WaitForAsync(() => !main.IsModuleLoading && main.CurrentContent is ShellViewModel, what: "unrelated module");
        Assert.False(main.CanReturnToGuide);
        session.ClickText("Home");
        session.ClickText("Intro guide");
        await session.WaitForAsync(() => main.CurrentContent is SetupGuideViewModel, what: "resume from Home");
        guide.SelectedIndex = guide.Lessons.Count - 1;
        session.Pump();
        session.ClickText("Review pack");
        await session.WaitForAsync(() => main.CurrentContent is SetLoaderViewModel { CanStagePack: true, SelectedSet: not null }, what: "pack");
        Assert.Equal(SetupGuideViewModel.PackName, ((SetLoaderViewModel)main.CurrentContent!).SelectedSet!.Name);
        session.Screenshot("pack");
        Assert.Empty(session.Services!.GetRequiredService<IPendingChangesService>().PendingGroups);
        Assert.Empty(session.Services.GetRequiredService<IPendingActionsService>().PendingActions);
    }

    [AvaloniaFact]
    public async Task MixedCaseRemoval_CannotConflictWithPendingReinstall()
    {
        var actions = new PendingActionsService();
        var entry = Modules.Software.Services.WindowsAppsCatalog.Entries.Single(e => e.Id == "microsoft.bingnews");
        actions.Stage(Modules.Software.Actions.SoftwareActionFactory.CreateAppxReinstall(entry));
        var row = new PackRemovalViewModel("MICROSOFT.BINGNEWS", entry, true, actions, () => { });
        Assert.False(row.CanInclude);
        Assert.Contains("Reinstall", row.State, StringComparison.Ordinal);
        await Task.CompletedTask;
    }

    [AvaloniaFact]
    public async Task EveryRegisteredModule_HasTeaching_AndChoicesReachPack()
    {
        var services = new ServiceCollection();
        App.ConfigureServices(services);
        await using var provider = services.BuildServiceProvider();
        var modules = provider.GetServices<IModule>().Select(m => new ModuleRegistration(m, new(true))).ToArray();
        IReadOnlyList<string>? excluded = null;
        var guide = new SetupGuideViewModel(modules, new Hardware(3), e => excluded = e, _ => { }, () => { });
        await guide.CheckDesktopAsync();
        Assert.True(guide.IsDesktop);
        Assert.Equal("Welcome", guide.Lessons[0].Label);
        Assert.All(modules, module => Assert.Equal(SidebarItemViewModel.GetIconSymbol(module.Module.Info.Icon),
            guide.Lessons.Single(l => l.ModuleName == module.Module.Info.Name).IconSymbol));
        Assert.Equal(modules.Length + 3, guide.Lessons.Count);
        Assert.All(guide.Lessons.Where(l => l.ModuleName is not null), lesson =>
            Assert.NotEqual(modules.Single(m => m.Module.Info.Name == lesson.ModuleName).Module.Info.Description, lesson.Body));
        using var session = UiSession.ForView(new SetupGuideView(), guide, "setup-guide", width: 960, height: 660);
        foreach (var theme in new[] { ThemeVariant.Dark, ThemeVariant.Light })
        {
            session.SetTheme(theme);
            guide.SelectedIndex = 0;
            for (var i = 0; i < guide.Lessons.Count; i++)
            {
                Assert.Equal(i, guide.SelectedIndex);
                Assert.Single(guide.Lessons, l => l.IsSelected);
                Assert.True(guide.Current.IsSelected);
                session.Screenshot($"{theme.Key}-{i:D2}");
                var card = session.Find<Border>(b => b.Classes.Contains("set-card-body") && ReferenceEquals(b.DataContext, guide.Current));
                var scroller = card.FindAncestorOfType<ScrollViewer>()!;
                var origin = card.TranslatePoint(default, scroller)!.Value;
                Assert.InRange(origin.Y, -0.5, scroller.Bounds.Height - card.Bounds.Height + 0.5);
                Assert.Equal(16, scroller.Bounds.Width - origin.X - card.Bounds.Width, 0.5);
                if (guide.HasNext) session.ClickText("Next");
            }
        }
        guide.Lessons.Single(l => l.ModuleName == "Software").IncludeRecommendations = false;
        session.ClickText("Review pack");
        Assert.Equal(["Software"], excluded);
        Assert.Empty(provider.GetRequiredService<IPendingActionsService>().PendingActions);
        Assert.Empty(provider.GetRequiredService<IPendingChangesService>().PendingGroups);
    }

    [AvaloniaTheory]
    [InlineData(9)]
    [InlineData(0)]
    public async Task UnsupportedMachine_CannotOpenOrStagePack(int chassis)
    {
        var opened = false;
        var guide = new SetupGuideViewModel([], new Hardware(chassis), _ => opened = true, _ => { }, () => { });
        await guide.CheckDesktopAsync();
        guide.ReviewPackCommand.Execute(null);
        Assert.False(opened);
        using var pack = CreatePack(new Hardware(chassis), new Packages(), out var pending, out var actions);
        pack.SelectSetCommand.Execute(pack.OptimizationPacks[0]);
        await pack.PackInspection;
        pack.StageIncludedCommand.Execute(null);
        Assert.False(pack.CanStagePack);
        Assert.Empty(pending.PendingGroups);
        Assert.Empty(actions.PendingActions);
    }

    [AvaloniaFact]
    public async Task Pack_StagesSelectedRemovalsSeparately_AndPreservesModuleChoices()
    {
        using var pack = CreatePack(new Hardware(3), new Packages(), out var pending, out var actions);
        pack.SelectSetCommand.Execute(pack.OptimizationPacks[0]);
        await pack.PackInspection;
        Assert.True(pack.CanStagePack);
        Assert.True(pack.AppRemovals[0].IsIncluded);
        Assert.False(pack.AppRemovals[1].CanInclude);
        pack.ExcludeModules(["Windows Annoyances"]);
        using var session = UiSession.ForView(new SetLoaderView(), pack, "desktop-grand-pack", width: 1100);
        foreach (var theme in new[] { ThemeVariant.Dark, ThemeVariant.Light })
        {
            session.SetTheme(theme);
            session.Screenshot(theme.Key.ToString()!);
        }
        session.ClickText("Stage 1 selection");
        Assert.Empty(pending.PendingGroups);
        Assert.Equal("appx-remove:microsoft.bingnews", Assert.Single(actions.PendingActions).ActionId);
        pack.StageIncludedCommand.Execute(null);
        Assert.Single(actions.PendingActions);
    }

    [AvaloniaFact]
    public async Task Pack_ExcludingSoftwareSkipsRemovals_AndUnknownAppsCannotStage()
    {
        using var pack = CreatePack(new Hardware(3), new Packages(), out var pending, out var actions);
        pack.SelectSetCommand.Execute(pack.OptimizationPacks[0]);
        await pack.PackInspection;
        pack.ExcludeModules(["Software"]);
        pack.StageIncludedCommand.Execute(null);
        Assert.Single(pending.PendingGroups);
        Assert.Empty(actions.PendingActions);
    }

    [AvaloniaFact]
    public async Task Pack_ChangingSelectionDuringInspection_DiscardsStaleResults()
    {
        var hardware = new DelayedHardware();
        using var pack = CreatePack(hardware, new Packages(), out var pending, out var actions);
        pack.SelectSetCommand.Execute(pack.OptimizationPacks[0]);
        var inspection = pack.PackInspection;
        Assert.False(pack.CanStagePack);
        pack.StageIncludedCommand.Execute(null);
        Assert.Empty(pending.PendingGroups);
        pack.SelectSetCommand.Execute(null);
        hardware.Result.SetResult(Hardware.Snapshot(3));
        await inspection;
        Assert.Empty(pack.AppRemovals);
        Assert.True(pack.HasNoSelection);
        Assert.Empty(actions.PendingActions);
    }

    [AvaloniaFact]
    public async Task Pack_FailedAppScan_DoesNotTreatFailureAsEmptyInventory()
    {
        using var pack = CreatePack(new Hardware(3), new Packages { Fail = true }, out var pending, out var actions);
        pack.SelectSetCommand.Execute(pack.OptimizationPacks[0]);
        await pack.PackInspection;
        pack.StageIncludedCommand.Execute(null);
        Assert.False(pack.CanStagePack);
        Assert.Contains("could not be read", pack.PackNotice, StringComparison.Ordinal);
        Assert.Empty(pending.PendingGroups);
        Assert.Empty(actions.PendingActions);
    }

    [AvaloniaFact]
    public async Task UnrelatedActionApply_PreservesSelectedPackRemovals()
    {
        using var pack = CreatePack(new Hardware(3), new Packages(), out _, out var actions);
        pack.SelectSetCommand.Execute(pack.OptimizationPacks[0]);
        await pack.PackInspection;
        actions.Stage(new() { ModuleId = "Software", ActionId = "install:example", DisplayName = "Unrelated install", Detail = "Test action" });
        var completion = new TaskCompletionSource<OperationResult<bool>>();
        var apply = actions.ApplyAllAsync(_ => completion.Task);
        Assert.True(pack.AppRemovals[0].IsIncluded);
        Assert.False(pack.AppRemovals[0].CanInclude);
        completion.SetResult(OperationResult<bool>.Success(true));
        await apply;
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();
        await pack.PackInspection;
        Assert.True(pack.AppRemovals[0].IsIncluded);
        Assert.True(pack.AppRemovals[0].CanInclude);
    }

    private static SetLoaderViewModel CreatePack(IHardwareDetectionService hardware, Packages packages,
        out PendingChangesService pending, out PendingActionsService actions)
    {
        pending = new(); actions = new();
        var definition = new SetDefinition
        {
            Name = SetupGuideViewModel.PackName, Description = "Desktop cleanup", Version = "1", Author = "ThisIsMyPC",
            Source = SetSource.BuiltIn, FilePath = "desktop-grand-pack.json", Category = SetCategory.OptimizationPack,
            DesktopOnly = true, AppRemovals = ["microsoft.bingnews", "unknown-app"],
            Entries = [new() { ModuleId = "Windows Annoyances", SettingId = "windows-tips", Value = "0", Description = "Reduce tips", Group = "Windows Annoyances" }],
        };
        return new SetLoaderViewModel(new() { Sets = [definition], Warnings = [] }, [new Inspector()], _ => new(true), pending,
            hardwareDetection: hardware, appxPackages: packages, pendingActions: actions);
    }

    private sealed class Inspector : ISetEntryInspector
    {
        public string ModuleId => "Windows Annoyances";
        public SetEntryState Inspect(SetEntry entry) => new() { CurrentValue = "1", CurrentDisplay = "Enabled", SettingDisplayName = "Windows tips", IsApplied = false };
        public ChangeGroup CreateChangeGroup(SetEntry entry) => new()
        {
            GroupId = "test-tips", Description = "Tips", DisplayName = "Windows tips", Changes = [new()
            {
                ModuleId = ModuleId, SettingId = entry.SettingId, DisplayName = "Windows tips", SystemLocation = "test",
                BeforeValue = "1", AfterValue = entry.Value, BeforeDisplay = "Enabled", AfterDisplay = "Disabled", ValueType = ChangeValueType.Registry_DWord, Category = ChangeCategory.Modify,
            }],
        };
    }

    private sealed class Hardware(int chassis) : IHardwareDetectionService
    {
        public static HardwareSnapshot Snapshot(int type) => new() { Facts = new() { FormFactor = new() { SmbiosChassisTypes = [type] } } };
        public Task<HardwareSnapshot> GetSnapshotAsync(CancellationToken cancellationToken = default) => Task.FromResult(Snapshot(chassis));
        public Task<HardwareSnapshot> RefreshAsync(CancellationToken cancellationToken = default) => GetSnapshotAsync(cancellationToken);
    }
    private sealed class DelayedHardware : IHardwareDetectionService
    {
        public TaskCompletionSource<HardwareSnapshot> Result { get; } = new();
        public Task<HardwareSnapshot> GetSnapshotAsync(CancellationToken cancellationToken = default) => Result.Task;
        public Task<HardwareSnapshot> RefreshAsync(CancellationToken cancellationToken = default) => Result.Task;
    }
    private sealed class Packages : IAppxPackageService
    {
        public bool Fail { get; init; }
        public Task<OperationResult<IReadOnlyList<AppxPackageInfo>>> EnumeratePackagesAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(Fail ? OperationResult<IReadOnlyList<AppxPackageInfo>>.Failure("Scan failed", ErrorCategory.ServiceUnavailable) :
                OperationResult<IReadOnlyList<AppxPackageInfo>>.Success([new("Microsoft.BingNews_1_x64_test", "Microsoft.BingNews_test", "News", "Microsoft", "1", false, AppxSignatureKind.Store, true)]));
        public Task<OperationResult<AppxPackageInfo>> QueryPackageAsync(string packageFullName, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<OperationResult<bool>> RemovePackageAsync(string packageFullName, bool allUsers = false, CancellationToken cancellationToken = default) => throw new InvalidOperationException("Tests must never remove apps.");
        public Task<OperationResult<bool>> DeprovisionPackageAsync(string packageFamilyName, CancellationToken cancellationToken = default) => throw new InvalidOperationException("Tests must never deprovision apps.");
    }
}
