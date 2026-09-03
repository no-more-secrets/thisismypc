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
    private static readonly string CommandBarKeyPath = Modules.Shell.ShellRegistryPaths.CommandBarKeyPath;

    /// <summary>Catalog id of the companion app the Start Menu tab offers.</summary>
    public const string ExplorerPatcherCatalogId = "explorerpatcher";

    /// <summary>ExplorerPatcher's own uninstall entry (ep_setup writes {CLSID}_ExplorerPatcher); present means installed.</summary>
    public const string ExplorerPatcherUninstallKeyPath =
        @"HKLM\SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\{D17F1E1A-5919-4427-8F89-A1A8503CA3EB}_ExplorerPatcher";

    public ObservableCollection<ShellSettingViewModel> GeneralSettings { get; } = [];
    public ObservableCollection<ShellSettingViewModel> FileExplorerSettings { get; } = [];
    public ObservableCollection<ShellSettingViewModel> TaskbarSettings { get; } = [];
    public ObservableCollection<ShellChoiceSettingViewModel> TaskbarChoiceSettings { get; } = [];
    public ObservableCollection<ShellSettingViewModel> DesktopSettings { get; } = [];
    public ObservableCollection<ShellSettingViewModel> StartMenuSettings { get; } = [];

    /// <summary>The ExplorerPatcher card on the Start Menu tab; null without an actions queue.</summary>
    public SoftwareAppViewModel? ExplorerPatcher { get; }

    public bool ShowExplorerPatcher => ExplorerPatcher is not null;

    private readonly IPendingActionsService? _pendingActionsService;

    private IEnumerable<ShellSettingViewModel> ToggleRows =>
        GeneralSettings.Concat(FileExplorerSettings).Concat(TaskbarSettings).Concat(DesktopSettings).Concat(StartMenuSettings);

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
        foreach (var row in TaskbarChoiceSettings)
            row.ApplySearch(value);
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

        // The Start Menu tab links the first companion installer: ExplorerPatcher
        // through the same one-way actions queue the Software page uses.
        if (pendingActionsService is not null
            && SoftwareCatalog.Entries.FirstOrDefault(e => e.Id == ExplorerPatcherCatalogId) is { } explorerPatcher)
        {
            var installed = registryService.KeyExists(ExplorerPatcherUninstallKeyPath) is { IsSuccess: true, Value: true };
            ExplorerPatcher = new SoftwareAppViewModel(explorerPatcher, installed, pendingActionsService);
            _pendingActionsService = pendingActionsService;
            pendingActionsService.PropertyChanged += OnPendingActionsPropertyChanged;
        }

        // Command bar style (Explorer visual, not a DWord preference; CLSID override)
        var taskbar = scanData.Taskbar;
        FileExplorerSettings.Add(new ShellSettingViewModel(
            label: "Use classic command bar",
            description: "Show the classic ribbon/command bar instead of the modern Windows 11 toolbar in File Explorer (requires Explorer restart)",
            systemPath: CommandBarKeyPath,
            isEnabled: taskbar.ClassicCommandBar,
            pendingChangesService: pendingChangesService,
            changeFactory: enable => TaskbarChangeFactory.CreateCommandBarToggle(taskbar, enable),
            readRegistryState: () =>
            {
                var result = registryService.KeyExists(CommandBarKeyPath);
                return result.IsSuccess && result.Value;
            }));

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

        GeneralSettings.Add(new ShellSettingViewModel(
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
