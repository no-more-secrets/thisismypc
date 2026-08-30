using System.Collections.ObjectModel;
using System.ComponentModel;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ThisIsMyPC.Core.Services;
using ThisIsMyPC.Modules.Software.Actions;
using ThisIsMyPC.Modules.Software.Models;

namespace ThisIsMyPC.App.ViewModels;

public sealed partial class SoftwareViewModel : ViewModelBase, IDisposable
{
    public const string AllCategories = "All categories";

    private readonly IPendingActionsService _pendingActionsService;
    private readonly List<SoftwareAppViewModel> _allApps;

    /// <summary>Catalog grouped by category (winutil-style), post search/filter.</summary>
    public ObservableCollection<SoftwareCategoryGroupViewModel> FilteredGroups { get; } = [];

    /// <summary>Flat view of every visible app, in group order.</summary>
    public IEnumerable<SoftwareAppViewModel> FilteredApps => FilteredGroups.SelectMany(g => g.Apps);

    public IReadOnlyList<WindowsAppViewModel> WindowsApps { get; }

    // The update check runs winget upgrade against the network; it can take
    // minutes on a slow machine and must never gate the page. It loads in the
    // background after construction and streams into the tab when done.
    public ObservableCollection<SoftwareUpdateViewModel> Updates { get; } = [];

    [ObservableProperty]
    private bool _isUpdatesLoading;

    [ObservableProperty]
    private bool _updatesLoadFailed;

    [ObservableProperty]
    private bool _hasUpdates;

    [ObservableProperty]
    private string _updatesSummary = string.Empty;

    public IReadOnlyList<string> Categories { get; }

    public bool InstalledStateKnown { get; }

    public bool AppxStateKnown { get; }

    public string? WingetVersion { get; }

    [ObservableProperty]
    private string _searchText = string.Empty;

    [ObservableProperty]
    private string _selectedCategory = AllCategories;

    public IReadOnlyList<string> InstallStateFilters { get; } = ["All", "Installed", "Not installed"];

    [ObservableProperty]
    private string _selectedInstallStateFilter = "All";

    [ObservableProperty]
    private string _updatesSearchText = string.Empty;

    public ObservableCollection<SoftwareUpdateViewModel> FilteredUpdates { get; } = [];

    [ObservableProperty]
    private string _windowsAppsSearchText = string.Empty;

    public ObservableCollection<WindowsAppViewModel> FilteredWindowsApps { get; } = [];

    public SoftwareViewModel(
        SoftwareScanData scanData, IPendingActionsService pendingActionsService,
        Core.Packages.IWingetService? wingetService = null)
    {
        ArgumentNullException.ThrowIfNull(scanData);
        _pendingActionsService = pendingActionsService;

        InstalledStateKnown = scanData.InstalledStateKnown;
        AppxStateKnown = scanData.AppxStateKnown;
        WingetVersion = scanData.WingetVersion;

        if (wingetService is not null)
        {
            IsUpdatesLoading = true;
            _ = LoadUpdatesAsync(wingetService);
        }

        WindowsApps = scanData.WindowsApps
            .OrderBy(e => e.Name, StringComparer.OrdinalIgnoreCase)
            .Select(entry => new WindowsAppViewModel(
                entry,
                isPresent: scanData.PresentAppxPackageIds.Contains(entry.PackageId),
                pendingActionsService))
            .ToList();

        _allApps = scanData.Catalog
            .OrderBy(e => e.Name, StringComparer.OrdinalIgnoreCase)
            .Select(entry => new SoftwareAppViewModel(
                entry,
                isInstalled: scanData.InstalledWingetIds.Contains(entry.WingetId),
                pendingActionsService))
            .ToList();

        Categories = new[] { AllCategories }
            .Concat(scanData.Catalog.Select(e => e.Category)
                .Distinct()
                .OrderBy(c => c, StringComparer.OrdinalIgnoreCase))
            .ToList();

        _pendingActionsService.PropertyChanged += OnPendingActionsPropertyChanged;
        RefreshFilter();
        RefreshWindowsAppsFilter();
    }

    private async Task LoadUpdatesAsync(Core.Packages.IWingetService wingetService)
    {
        var result = await wingetService.ListUpgradableAsync().ConfigureAwait(false);

        Dispatcher.UIThread.Post(() =>
        {
            if (_disposed)
                return;

            IsUpdatesLoading = false;
            if (!result.IsSuccess)
            {
                UpdatesLoadFailed = true;
                return;
            }

            foreach (var package in result.Value!.OrderBy(p => p.Name, StringComparer.OrdinalIgnoreCase))
                Updates.Add(new SoftwareUpdateViewModel(package, _pendingActionsService));
            RefreshUpdatesFilter();

            HasUpdates = Updates.Count > 0;
            UpdatesSummary = Updates.Count switch
            {
                0 => "Everything winget manages is up to date.",
                1 => "1 update available.",
                var n => $"{n} updates available.",
            };
        });
    }

    partial void OnSearchTextChanged(string value) => RefreshFilter();

    partial void OnSelectedCategoryChanged(string value) => RefreshFilter();

    partial void OnSelectedInstallStateFilterChanged(string value) => RefreshFilter();

    partial void OnUpdatesSearchTextChanged(string value) => RefreshUpdatesFilter();

    partial void OnWindowsAppsSearchTextChanged(string value) => RefreshWindowsAppsFilter();

    private void RefreshUpdatesFilter()
    {
        FilteredUpdates.Clear();
        foreach (var update in Updates)
        {
            if (UpdatesSearchText.Length == 0
                || update.Name.Contains(UpdatesSearchText, StringComparison.OrdinalIgnoreCase)
                || update.PackageId.Contains(UpdatesSearchText, StringComparison.OrdinalIgnoreCase))
            {
                FilteredUpdates.Add(update);
            }
        }
    }

    private void RefreshWindowsAppsFilter()
    {
        FilteredWindowsApps.Clear();
        foreach (var app in WindowsApps)
        {
            if (WindowsAppsSearchText.Length == 0
                || app.Name.Contains(WindowsAppsSearchText, StringComparison.OrdinalIgnoreCase)
                || app.PackageId.Contains(WindowsAppsSearchText, StringComparison.OrdinalIgnoreCase))
            {
                FilteredWindowsApps.Add(app);
            }
        }
    }

    private void RefreshFilter()
    {
        FilteredGroups.Clear();

        foreach (var categoryGroup in _allApps
            .Where(MatchesFilter)
            .GroupBy(a => a.Category)
            .OrderBy(g => g.Key, StringComparer.OrdinalIgnoreCase))
        {
            FilteredGroups.Add(new SoftwareCategoryGroupViewModel
            {
                Category = categoryGroup.Key,
                Apps = categoryGroup.ToList(),
            });
        }
    }

    private bool MatchesFilter(SoftwareAppViewModel app)
    {
        if (SelectedCategory != AllCategories && app.Category != SelectedCategory)
            return false;

        if (SelectedInstallStateFilter == "Installed" && !app.IsInstalled)
            return false;
        if (SelectedInstallStateFilter == "Not installed" && app.IsInstalled)
            return false;

        return SearchText.Length == 0
            || app.Name.Contains(SearchText, StringComparison.OrdinalIgnoreCase)
            || app.Description.Contains(SearchText, StringComparison.OrdinalIgnoreCase)
            || app.WingetId.Contains(SearchText, StringComparison.OrdinalIgnoreCase);
    }

    private void OnPendingActionsPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        // Apply/discard empties the queue outside this view — rows must drop
        // their queued state.
        if (e.PropertyName is nameof(IPendingActionsService.PendingActions))
        {
            if (Dispatcher.UIThread.CheckAccess())
                RefreshQueuedStates();
            else
                Dispatcher.UIThread.Post(RefreshQueuedStates);
        }
    }

    private void RefreshQueuedStates()
    {
        foreach (var app in _allApps)
            app.RefreshQueuedState();
        foreach (var app in WindowsApps)
            app.RefreshQueuedState();
        foreach (var update in Updates)
            update.RefreshQueuedState();
    }

    [RelayCommand]
    private void QueueAllUpdates()
    {
        // Acts on what the user can see; a search narrows Update all with it.
        foreach (var update in FilteredUpdates)
            update.Queue();
    }

    /// <summary>
    /// Flips row state for actions that just succeeded, so an installed app's
    /// button reads Uninstall without a rescan. Called by the host after Apply.
    /// </summary>
    public void ApplyActionResults(Core.Actions.ActionBatchResult result)
    {
        ArgumentNullException.ThrowIfNull(result);

        foreach (var action in result.Succeeded)
        {
            foreach (var app in _allApps)
                app.HandleActionSucceeded(action.ActionId);
            foreach (var app in WindowsApps)
                app.HandleActionSucceeded(action.ActionId);
            foreach (var update in Updates)
                update.HandleActionSucceeded(action.ActionId);
        }
    }

    private bool _disposed;

    public void Dispose()
    {
        _disposed = true;
        _pendingActionsService.PropertyChanged -= OnPendingActionsPropertyChanged;
    }
}

/// <summary>One category section of the catalog grid.</summary>
public sealed class SoftwareCategoryGroupViewModel
{
    public required string Category { get; init; }
    public required IReadOnlyList<SoftwareAppViewModel> Apps { get; init; }
}

public sealed partial class SoftwareAppViewModel : ViewModelBase
{
    private readonly SoftwareCatalogEntry _entry;
    private readonly IPendingActionsService _pendingActionsService;

    public SoftwareAppViewModel(
        SoftwareCatalogEntry entry, bool isInstalled, IPendingActionsService pendingActionsService)
    {
        ArgumentNullException.ThrowIfNull(pendingActionsService);
        _entry = entry;
        _pendingActionsService = pendingActionsService;
        _isInstalled = isInstalled;
        _isQueued = pendingActionsService.IsStaged(ActionId);
    }

    public string Name => _entry.Name;
    public string Description => _entry.Description;
    public string Category => _entry.Category;
    public string WingetId => _entry.WingetId;
    public bool IsOpenSource => _entry.IsOpenSource;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ActionButtonText))]
    private bool _isInstalled;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ActionButtonText))]
    private bool _isQueued;

    private string ActionId => IsInstalled
        ? SoftwareActionFactory.UninstallPrefix + _entry.Id
        : SoftwareActionFactory.InstallPrefix + _entry.Id;

    public string ActionButtonText => IsQueued
        ? "Queued"
        : IsInstalled ? "Uninstall" : "Install";

    [RelayCommand]
    private void ToggleQueue()
    {
        if (IsQueued)
        {
            _pendingActionsService.Unstage(ActionId);
        }
        else
        {
            _pendingActionsService.Stage(IsInstalled
                ? SoftwareActionFactory.CreateUninstall(_entry)
                : SoftwareActionFactory.CreateInstall(_entry));
        }

        RefreshQueuedState();
    }

    public void RefreshQueuedState() => IsQueued = _pendingActionsService.IsStaged(ActionId);

    public void HandleActionSucceeded(string actionId)
    {
        if (actionId == SoftwareActionFactory.InstallPrefix + _entry.Id)
            IsInstalled = true;
        else if (actionId == SoftwareActionFactory.UninstallPrefix + _entry.Id)
            IsInstalled = false;
        else
            return;

        RefreshQueuedState();
    }
}

public sealed partial class SoftwareUpdateViewModel : ViewModelBase
{
    private readonly Core.Packages.UpgradableWingetPackage _package;
    private readonly IPendingActionsService _pendingActionsService;

    public SoftwareUpdateViewModel(
        Core.Packages.UpgradableWingetPackage package, IPendingActionsService pendingActionsService)
    {
        ArgumentNullException.ThrowIfNull(package);
        ArgumentNullException.ThrowIfNull(pendingActionsService);
        _package = package;
        _pendingActionsService = pendingActionsService;
        _isQueued = pendingActionsService.IsStaged(ActionId);
    }

    public string Name => _package.Name;
    public string PackageId => _package.PackageId;
    public string VersionText => $"{_package.InstalledVersion} to {_package.AvailableVersion}";

    private string ActionId => SoftwareActionFactory.UpgradePrefix + _package.PackageId;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ActionButtonText))]
    [NotifyPropertyChangedFor(nameof(CanAct))]
    private bool _isQueued;

    // Done stays done until the next scan; the row keeps its place so the list
    // does not reshuffle mid-review.
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ActionButtonText))]
    [NotifyPropertyChangedFor(nameof(CanAct))]
    private bool _isUpdated;

    public bool CanAct => !IsUpdated;

    public string ActionButtonText => IsUpdated
        ? "Updated"
        : IsQueued ? "Queued" : "Update";

    [RelayCommand]
    private void ToggleQueue()
    {
        if (IsUpdated)
            return;

        if (IsQueued)
            _pendingActionsService.Unstage(ActionId);
        else
            Queue();

        RefreshQueuedState();
    }

    public void Queue()
    {
        if (IsUpdated)
            return;

        _pendingActionsService.Stage(SoftwareActionFactory.CreateUpgrade(_package));
        RefreshQueuedState();
    }

    public void RefreshQueuedState() => IsQueued = _pendingActionsService.IsStaged(ActionId);

    public void HandleActionSucceeded(string actionId)
    {
        if (actionId != ActionId)
            return;

        IsUpdated = true;
        RefreshQueuedState();
    }
}

public sealed partial class WindowsAppViewModel : ViewModelBase
{
    private readonly WindowsAppEntry _entry;
    private readonly IPendingActionsService _pendingActionsService;

    public WindowsAppViewModel(
        WindowsAppEntry entry, bool isPresent, IPendingActionsService pendingActionsService)
    {
        ArgumentNullException.ThrowIfNull(pendingActionsService);
        _entry = entry;
        _pendingActionsService = pendingActionsService;
        _isPresent = isPresent;
        _isQueued = pendingActionsService.IsStaged(ActionId);
    }

    public string Name => _entry.Name;
    public string Description => _entry.Description;
    public string Category => _entry.Category;
    public string PackageId => _entry.PackageId;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ActionButtonText))]
    [NotifyPropertyChangedFor(nameof(CanAct))]
    private bool _isPresent;

    // A present app can only be removed; an absent one only reinstalled (Store id permitting).
    public bool CanAct => IsPresent || _entry.CanReinstall;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ActionButtonText))]
    private bool _isQueued;

    private string ActionId => IsPresent
        ? SoftwareActionFactory.AppxRemovePrefix + _entry.Id
        : SoftwareActionFactory.AppxReinstallPrefix + _entry.Id;

    public string ActionButtonText => IsQueued
        ? "Queued"
        : IsPresent ? "Remove" : "Reinstall";

    [RelayCommand]
    private void ToggleQueue()
    {
        if (IsQueued)
        {
            _pendingActionsService.Unstage(ActionId);
        }
        else
        {
            _pendingActionsService.Stage(IsPresent
                ? SoftwareActionFactory.CreateAppxRemove(_entry)
                : SoftwareActionFactory.CreateAppxReinstall(_entry));
        }

        RefreshQueuedState();
    }

    public void RefreshQueuedState() => IsQueued = _pendingActionsService.IsStaged(ActionId);

    public void HandleActionSucceeded(string actionId)
    {
        if (actionId == SoftwareActionFactory.AppxRemovePrefix + _entry.Id)
            IsPresent = false;
        else if (actionId == SoftwareActionFactory.AppxReinstallPrefix + _entry.Id)
            IsPresent = true;
        else
            return;

        RefreshQueuedState();
    }
}
