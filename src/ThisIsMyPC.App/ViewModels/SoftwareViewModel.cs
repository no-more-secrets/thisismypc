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

    public ObservableCollection<SoftwareAppViewModel> FilteredApps { get; } = [];

    public IReadOnlyList<string> Categories { get; }

    public bool InstalledStateKnown { get; }

    public string? WingetVersion { get; }

    [ObservableProperty]
    private string _searchText = string.Empty;

    [ObservableProperty]
    private string _selectedCategory = AllCategories;

    public SoftwareViewModel(SoftwareScanData scanData, IPendingActionsService pendingActionsService)
    {
        ArgumentNullException.ThrowIfNull(scanData);
        _pendingActionsService = pendingActionsService;

        InstalledStateKnown = scanData.InstalledStateKnown;
        WingetVersion = scanData.WingetVersion;

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
    }

    partial void OnSearchTextChanged(string value) => RefreshFilter();

    partial void OnSelectedCategoryChanged(string value) => RefreshFilter();

    private void RefreshFilter()
    {
        FilteredApps.Clear();

        foreach (var app in _allApps)
        {
            if (SelectedCategory != AllCategories && app.Category != SelectedCategory)
                continue;

            if (SearchText.Length > 0
                && !app.Name.Contains(SearchText, StringComparison.OrdinalIgnoreCase)
                && !app.Description.Contains(SearchText, StringComparison.OrdinalIgnoreCase)
                && !app.WingetId.Contains(SearchText, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            FilteredApps.Add(app);
        }
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
    }

    public void Dispose()
    {
        _pendingActionsService.PropertyChanged -= OnPendingActionsPropertyChanged;
    }
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
        IsInstalled = isInstalled;
        _isQueued = pendingActionsService.IsStaged(ActionId);
    }

    public string Name => _entry.Name;
    public string Description => _entry.Description;
    public string Category => _entry.Category;
    public string WingetId => _entry.WingetId;
    public bool IsOpenSource => _entry.IsOpenSource;
    public bool IsInstalled { get; }

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
}
