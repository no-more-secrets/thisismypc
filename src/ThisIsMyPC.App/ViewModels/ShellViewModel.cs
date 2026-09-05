using System.Collections.ObjectModel;
using System.ComponentModel;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using ThisIsMyPC.Core.Services;
using ThisIsMyPC.Modules.Shell.Changes;
using ThisIsMyPC.Modules.Shell.Models;
using ThisIsMyPC.Modules.Software.Services;

namespace ThisIsMyPC.App.ViewModels;

public partial class ShellViewModel : ViewModelBase, ISearchFocusTarget, IDisposable
{
    private static readonly string AdvancedKeyPath = Modules.Shell.ShellRegistryPaths.AdvancedKeyPath;
    private static readonly string ClassicContextMenuKeyPath = Modules.Shell.ShellRegistryPaths.ClassicContextMenuKeyPath;

    /// <summary>Catalog id of the ExplorerPatcher installer card on the General tab.</summary>
    public const string ExplorerPatcherCatalogId = "explorerpatcher";

    public ObservableCollection<ShellSettingViewModel> GeneralSettings { get; } = [];
    public ObservableCollection<ShellSettingViewModel> FileExplorerSettings { get; } = [];
    public ObservableCollection<ShellSettingViewModel> TaskbarSettings { get; } = [];
    public ObservableCollection<ShellChoiceSettingViewModel> TaskbarChoiceSettings { get; } = [];
    public ObservableCollection<ShellSettingViewModel> DesktopSettings { get; } = [];
    public ObservableCollection<ShellSettingViewModel> StartMenuSettings { get; } = [];

    // ExplorerPatcher's own settings, one block per tab under its heading,
    // grouped by the page they sit on in ExplorerPatcher's own window.
    // Empty unless ExplorerPatcher is installed.
    public ObservableCollection<ExplorerPatcherGroupViewModel> GeneralPatcherGroups { get; } = [];
    public ObservableCollection<ExplorerPatcherGroupViewModel> FileExplorerPatcherGroups { get; } = [];
    public ObservableCollection<ExplorerPatcherGroupViewModel> TaskbarPatcherGroups { get; } = [];
    public ObservableCollection<ExplorerPatcherGroupViewModel> DesktopPatcherGroups { get; } = [];
    public ObservableCollection<ExplorerPatcherGroupViewModel> StartMenuPatcherGroups { get; } = [];

    /// <summary>True while ExplorerPatcher is installed, so its rows are worth showing.</summary>
    public bool ShowPatcherSettings { get; }

    /// <summary>The General tab's block also carries the installer card, so it shows whenever there is a card.</summary>
    public bool ShowGeneralPatcher => ShowExplorerPatcher || (ShowPatcherSettings && GeneralPatcherGroups.Count > 0);
    public bool ShowFileExplorerPatcher => ShowPatcherSettings && FileExplorerPatcherGroups.Count > 0;
    public bool ShowTaskbarPatcher => ShowPatcherSettings && TaskbarPatcherGroups.Count > 0;
    public bool ShowDesktopPatcher => ShowPatcherSettings && DesktopPatcherGroups.Count > 0;
    public bool ShowStartMenuPatcher => ShowPatcherSettings && StartMenuPatcherGroups.Count > 0;

    private IEnumerable<ExplorerPatcherGroupViewModel> PatcherGroups =>
        GeneralPatcherGroups.Concat(FileExplorerPatcherGroups).Concat(TaskbarPatcherGroups)
            .Concat(DesktopPatcherGroups).Concat(StartMenuPatcherGroups);

    /// <summary>Every ExplorerPatcher toggle row, across tabs.</summary>
    public IEnumerable<ShellSettingViewModel> PatcherToggles => PatcherGroups.SelectMany(g => g.Toggles);

    /// <summary>Every ExplorerPatcher choice row, across tabs.</summary>
    public IEnumerable<ShellChoiceSettingViewModel> PatcherChoices => PatcherGroups.SelectMany(g => g.Choices);

    /// <summary>Heading above each tab's ExplorerPatcher rows.</summary>
    public static string PatcherHeading => "ExplorerPatcher";

    /// <summary>
    /// Set when the installed ExplorerPatcher is not the release these rows
    /// were built from. The settings are pinned to one version on purpose, so
    /// a difference is worth saying rather than hiding.
    /// </summary>
    public string PatcherVersionNote { get; } = string.Empty;

    public bool ShowPatcherVersionNote => PatcherVersionNote.Length > 0;

    /// <summary>The ExplorerPatcher installer card at the top of the General tab's block; null without an actions queue.</summary>
    public SoftwareAppViewModel? ExplorerPatcher { get; }

    public bool ShowExplorerPatcher => ExplorerPatcher is not null;

    private readonly IPendingActionsService? _pendingActionsService;

    private IEnumerable<ShellSettingViewModel> ToggleRows =>
        GeneralSettings.Concat(FileExplorerSettings).Concat(TaskbarSettings).Concat(DesktopSettings).Concat(StartMenuSettings);

    private IEnumerable<ShellChoiceSettingViewModel> ChoiceRows =>
        TaskbarChoiceSettings.Concat(PatcherChoices);

    private ObservableCollection<ExplorerPatcherGroupViewModel> PatcherGroupsFor(ShellSection section) => section switch
    {
        ShellSection.General => GeneralPatcherGroups,
        ShellSection.Taskbar => TaskbarPatcherGroups,
        ShellSection.Desktop => DesktopPatcherGroups,
        ShellSection.StartMenu => StartMenuPatcherGroups,
        _ => FileExplorerPatcherGroups,
    };

    /// <summary>The catalog is ordered by tab, then group, so a new heading opens a new group.</summary>
    private ExplorerPatcherGroupViewModel PatcherGroupFor(ShellSection section, string heading)
    {
        var groups = PatcherGroupsFor(section);
        if (groups.Count > 0 && groups[^1].Heading == heading)
            return groups[^1];
        var group = new ExplorerPatcherGroupViewModel(heading);
        groups.Add(group);
        return group;
    }

    private ObservableCollection<ShellSettingViewModel> RowsFor(ShellSection section) => section switch
    {
        ShellSection.General => GeneralSettings,
        ShellSection.Taskbar => TaskbarSettings,
        ShellSection.Desktop => DesktopSettings,
        ShellSection.StartMenu => StartMenuSettings,
        _ => FileExplorerSettings,
    };

    [ObservableProperty]
    private string _searchText = string.Empty;

    partial void OnSearchTextChanged(string value)
    {
        foreach (var row in ToggleRows)
            row.ApplySearch(value);
        foreach (var row in ChoiceRows)
            row.ApplySearch(value);
        foreach (var group in PatcherGroups)
            group.ApplySearch(value);
    }

    public ShellViewModel(
        ShellScanData scanData,
        IPendingChangesService pendingChangesService,
        IRegistryService registryService,
        IPendingActionsService? pendingActionsService = null)
    {
        // Explorer preferences, each on its own tab
        foreach (var pref in scanData.ExplorerPreferences)
        {
            var capturedPref = pref;
            RowsFor(pref.Section).Add(new ShellSettingViewModel(
                capturedPref,
                pendingChangesService,
                readRegistryState: () => ReadExplorerPrefFromRegistry(registryService, capturedPref)));
        }

        // The General tab's ExplorerPatcher block opens with its installer card,
        // through the same one-way actions queue the Software page uses.
        if (pendingActionsService is not null
            && SoftwareCatalog.Entries.FirstOrDefault(e => e.Id == ExplorerPatcherCatalogId) is { } explorerPatcher)
        {
            ExplorerPatcher = new SoftwareAppViewModel(explorerPatcher, scanData.ExplorerPatcherInstalled, pendingActionsService);
            _pendingActionsService = pendingActionsService;
            pendingActionsService.PropertyChanged += OnPendingActionsPropertyChanged;
        }

        // ExplorerPatcher's settings. They are plain registry values that its
        // own monitor thread watches, so the app writes them like any other
        // preference; the ones it reads only at startup carry a restart.
        ShowPatcherSettings = scanData.ExplorerPatcherInstalled;
        if (scanData.ExplorerPatcherVersionDiffers)
        {
            PatcherVersionNote =
                $"These settings match ExplorerPatcher {scanData.ExplorerPatcherCatalogVersion}; "
                + $"version {scanData.ExplorerPatcherVersion} is installed. A setting may have moved in between.";
        }
        foreach (var setting in scanData.ExplorerPatcherSettings)
        {
            if (!setting.IsAvailable)
                continue;

            var captured = setting;
            var rows = PatcherGroupFor(captured.Section, captured.GroupHeading).Rows;
            var description = captured.Description;

            int? ReadLive()
            {
                var read = registryService.ReadDWord(captured.RegistryKeyPath, captured.RegistryValueName);
                return read.IsSuccess ? read.Value : null;
            }

            if (captured.Kind == Modules.Shell.Models.ExplorerPatcherSettingKind.Choice)
            {
                rows.Add(new ShellChoiceSettingViewModel(
                    captured.DisplayName,
                    description,
                    captured.SystemLocation,
                    [.. captured.Options.Select(o => new ShellChoiceOption(o.Value, o.DisplayName))],
                    captured.EffectiveValue,
                    pendingChangesService,
                    changeFactory: newValue => ExplorerPatcherChangeFactory.Create(captured, ReadLive(), newValue),
                    readRegistryValue: () => ReadLive() ?? captured.DefaultValue,
                    rehydrateSettingId: ExplorerPatcherChangeFactory.SettingIdPrefix + captured.RegistryValueName));
            }
            else
            {
                rows.Add(new ShellSettingViewModel(
                    captured.DisplayName,
                    description,
                    captured.SystemLocation,
                    captured.IsOn,
                    pendingChangesService,
                    changeFactory: on => ExplorerPatcherChangeFactory.Create(captured, ReadLive(), captured.ValueFor(on)),
                    readRegistryState: () =>
                    {
                        var live = ReadLive() ?? captured.DefaultValue;
                        return captured.Kind == Modules.Shell.Models.ExplorerPatcherSettingKind.InvertedToggle
                            ? live == 0
                            : live != 0;
                    }));
            }
        }

        var taskbar = scanData.Taskbar;
        // Taskbar settings

        TaskbarSettings.Add(new ShellSettingViewModel(
            label: "Taskbar alignment (Left)",
            description: "Align taskbar icons to the left instead of center",
            systemPath: $@"{AdvancedKeyPath}\TaskbarAl",
            isEnabled: taskbar.Alignment == 0,
            pendingChangesService: pendingChangesService,
            changeFactory: enable => TaskbarChangeFactory.CreateAlignmentChange(taskbar, enable ? 0 : 1),
            readRegistryState: () =>
            {
                var result = registryService.ReadDWord(AdvancedKeyPath, "TaskbarAl");
                return result.IsSuccess && result.Value == 0;
            }));

        TaskbarSettings.Add(new ShellSettingViewModel(
            label: "Taskbar widgets",
            description: "Show or hide the Widgets button on the taskbar",
            systemPath: $@"{AdvancedKeyPath}\TaskbarDa",
            isEnabled: taskbar.WidgetsEnabled,
            pendingChangesService: pendingChangesService,
            changeFactory: enable => TaskbarChangeFactory.CreateWidgetsToggle(taskbar, enable),
            readRegistryState: () =>
            {
                var result = registryService.ReadDWord(AdvancedKeyPath, "TaskbarDa");
                return result.IsSuccess && result.Value == 1;
            }));

        TaskbarChoiceSettings.Add(new ShellChoiceSettingViewModel(
            label: "Taskbar search",
            description: "How search appears on the taskbar (takes effect after Explorer restarts)",
            systemPath: $@"{Modules.Shell.ShellRegistryPaths.SearchKeyPath}\SearchboxTaskbarMode",
            options: TaskbarChangeFactory.SearchboxModeNames
                .OrderBy(p => p.Key)
                .Select(p => new ShellChoiceOption(p.Key, p.Value))
                .ToList(),
            currentValue: taskbar.SearchboxMode,
            pendingChangesService: pendingChangesService,
            changeFactory: mode => TaskbarChangeFactory.CreateSearchboxModeChange(taskbar, mode),
            readRegistryValue: () =>
            {
                var result = registryService.ReadDWord(Modules.Shell.ShellRegistryPaths.SearchKeyPath, "SearchboxTaskbarMode");
                return result.IsSuccess ? result.Value! : 3;
            }));

        TaskbarChoiceSettings.Add(new ShellChoiceSettingViewModel(
            label: "Combine taskbar buttons",
            description: "When windows of the same app share one taskbar button (takes effect after Explorer restarts)",
            systemPath: $@"{AdvancedKeyPath}\TaskbarGlomLevel",
            options: TaskbarChangeFactory.ButtonCombiningNames
                .OrderBy(p => p.Key)
                .Select(p => new ShellChoiceOption(p.Key, p.Value))
                .ToList(),
            currentValue: taskbar.ButtonCombining,
            pendingChangesService: pendingChangesService,
            changeFactory: level => TaskbarChangeFactory.CreateButtonCombiningChange(taskbar, level),
            readRegistryValue: () =>
            {
                var result = registryService.ReadDWord(AdvancedKeyPath, "TaskbarGlomLevel");
                return result.IsSuccess ? result.Value! : 0;
            }));

        // Classic context menu is one of the most-wanted Windows 11 changes, so
        // it leads the General tab rather than trailing the read preferences.
        GeneralSettings.Insert(0, new ShellSettingViewModel(
            label: "Classic context menu",
            description: "Use Windows 10-style full context menu instead of the compact Windows 11 menu (requires Explorer restart)",
            systemPath: ClassicContextMenuKeyPath,
            isEnabled: taskbar.ClassicContextMenu,
            pendingChangesService: pendingChangesService,
            changeFactory: enable => TaskbarChangeFactory.CreateClassicContextMenuToggle(taskbar, enable),
            readRegistryState: () =>
            {
                var result = registryService.KeyExists(ClassicContextMenuKeyPath);
                return result.IsSuccess && result.Value;
            }));
    }

    private void OnPendingActionsPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        // Apply/discard empties the queue outside this view; the card drops its queued state.
        if (e.PropertyName is not nameof(IPendingActionsService.PendingActions) || ExplorerPatcher is null)
            return;
        if (Dispatcher.UIThread.CheckAccess())
            ExplorerPatcher.RefreshQueuedState();
        else
            Dispatcher.UIThread.Post(ExplorerPatcher.RefreshQueuedState);
    }

    /// <summary>After the actions batch: an installed ExplorerPatcher shows as installed.</summary>
    public void ApplyActionResults(Core.Actions.ActionBatchResult result)
    {
        ArgumentNullException.ThrowIfNull(result);
        if (ExplorerPatcher is null)
            return;
        foreach (var action in result.Succeeded)
            ExplorerPatcher.HandleActionSucceeded(action.ActionId);
    }

    public void Dispose()
    {
        if (_pendingActionsService is not null)
            _pendingActionsService.PropertyChanged -= OnPendingActionsPropertyChanged;
    }

    private static bool ReadExplorerPrefFromRegistry(IRegistryService registryService, ExplorerPreference pref)
    {
        if (pref.ValueType == Core.Changes.ChangeValueType.Registry_String)
        {
            // Absent string value = the delete-to-restore state (AbsentValue)
            var read = registryService.ReadString(pref.RegistryKeyPath, pref.RegistryValueName);
            var current = read.IsSuccess ? read.Value! : Modules.Shell.ShellRegistryPaths.AbsentValue;
            return current == pref.EnabledValue;
        }

        var result = registryService.ReadDWord(pref.RegistryKeyPath, pref.RegistryValueName);
        if (!result.IsSuccess)
            return pref.IsEnabled; // fallback to scan value if read fails
        return result.Value.ToString() == pref.EnabledValue;
    }
}
