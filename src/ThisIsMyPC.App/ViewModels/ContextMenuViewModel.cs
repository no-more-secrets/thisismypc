using System.Collections.ObjectModel;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ThisIsMyPC.Core.Services;
using ThisIsMyPC.Interop.Win32.Registry;
using ThisIsMyPC.Modules.Shell.Changes;
using ThisIsMyPC.Modules.Shell.Models;
using ThisIsMyPC.Modules.Shell.Services;

namespace ThisIsMyPC.App.ViewModels;

public partial class ContextMenuViewModel : ViewModelBase, IDisposable
{
    // Backing store for handler type filtering — populated once, used to repopulate collections
    private readonly List<(ContextMenuHandlerViewModel Vm, HashSet<ContextMenuTab> Tabs)> _allHandlerEntries = [];
    private readonly IPendingChangesService _pendingChangesService;
    private readonly IRegistryService _registryService;
    private readonly ContextMenuScanner? _scanner;

    public ObservableCollection<ContextMenuHandlerViewModel> FileHandlers { get; } = [];
    public ObservableCollection<ContextMenuHandlerViewModel> FolderHandlers { get; } = [];
    public ObservableCollection<ContextMenuHandlerViewModel> FolderBackgroundHandlers { get; } = [];
    public ObservableCollection<ContextMenuHandlerViewModel> DesktopHandlers { get; } = [];
    public ObservableCollection<ContextMenuHandlerViewModel> MiscHandlers { get; } = [];
    public ObservableCollection<ContextMenuHandlerViewModel> MultiHandlers { get; } = [];

    // Per File Type tab
    public ObservableCollection<ContextMenuHandlerViewModel> PerFileTypeHandlers { get; } = [];

    [ObservableProperty]
    private IReadOnlyList<string> _availableExtensions = [];

    [ObservableProperty]
    private string? _selectedExtension;

    [ObservableProperty]
    private bool _isFileTypeScanning;

    // Misc tab sub-groups by surface
    public ObservableCollection<ContextMenuHandlerViewModel> DriveMiscHandlers { get; } = [];
    public ObservableCollection<ContextMenuHandlerViewModel> ThisPcMiscHandlers { get; } = [];
    public ObservableCollection<ContextMenuHandlerViewModel> NetworkMiscHandlers { get; } = [];
    public ObservableCollection<ContextMenuHandlerViewModel> RecycleBinMiscHandlers { get; } = [];

    [ObservableProperty]
    private bool _isRegistryViewMode;

    [ObservableProperty]
    private HandlerType? _handlerTypeFilter;

    [ObservableProperty]
    private bool _isOrphanFilterActive;

    [ObservableProperty]
    private int _orphanCount;

    [ObservableProperty]
    private int _comHandlerCount;

    [ObservableProperty]
    private int _staticVerbCount;

    [ObservableProperty]
    private int _modernPackagedCount;

    [ObservableProperty]
    private int _dualRegisteredCount;

    [ObservableProperty]
    private int _dragDropHandlerCount;

    public string ScanSummary => $"{ComHandlerCount} COM handlers, {StaticVerbCount} static verbs, {ModernPackagedCount} modern, {DragDropHandlerCount} drag-drop, {OrphanCount} orphaned, {DualRegisteredCount} dual-registered";

    public bool IsClassicMenuActive { get; }
    public string ClassicMenuBannerText { get; }

    public string FileHandlerCount => $"File ({FileHandlers.Count})";
    public string FolderHandlerCount => $"Folder ({FolderHandlers.Count})";
    public string FolderBackgroundHandlerCount => $"Folder Background ({FolderBackgroundHandlers.Count})";
    public string DesktopHandlerCount => $"Desktop ({DesktopHandlers.Count})";
    public string MiscHandlerCount => $"Misc ({MiscHandlers.Count})";
    public string MultiHandlerCount => $"Multi ({MultiHandlers.Count})";

    public ContextMenuViewModel(
        IReadOnlyList<ContextMenuHandler> handlers,
        IPendingChangesService pendingChangesService,
        IRegistryService registryService,
        ContextMenuScanner? scanner = null)
    {
        _pendingChangesService = pendingChangesService;
        _registryService = registryService;
        _scanner = scanner;

        // Detect classic context menu shim
        var shimKeyResult = registryService.KeyExists(Modules.Shell.ShellRegistryPaths.ClassicContextMenuKeyPath);
        if (shimKeyResult.IsSuccess && shimKeyResult.Value)
        {
            var shimValueResult = registryService.ReadString(Modules.Shell.ShellRegistryPaths.ClassicContextMenuKeyPath, string.Empty);
            IsClassicMenuActive = shimValueResult.IsSuccess && shimValueResult.Value == string.Empty;
        }

        ClassicMenuBannerText = IsClassicMenuActive
            ? "Classic menu mode -- all legacy handlers visible in top-level menu"
            : string.Empty;

        BuildContextMenuHandlerTabs(handlers, pendingChangesService, registryService);

        // Discover file extensions with custom context menu registrations
        if (scanner is not null)
        {
            var discovery = new FileExtensionDiscovery(registryService);
            var extResult = discovery.DiscoverExtensions();
            if (extResult.IsSuccess)
                AvailableExtensions = extResult.Value!;
        }
    }

    private void BuildContextMenuHandlerTabs(
        IReadOnlyList<ContextMenuHandler> handlers,
        IPendingChangesService pendingChangesService,
        IRegistryService registryService)
    {
        // Unique key: CLSID for COM handlers, verb dedup key for static verbs
        var vmMap = new Dictionary<string, ContextMenuHandlerViewModel>(StringComparer.OrdinalIgnoreCase);
        var vmTabs = new Dictionary<string, HashSet<ContextMenuTab>>(StringComparer.OrdinalIgnoreCase);
        var vmScopes = new Dictionary<string, HashSet<string>>(StringComparer.OrdinalIgnoreCase);

        foreach (var handler in handlers)
        {
            var key = MakeHandlerKey(handler);

            if (!vmMap.TryGetValue(key, out var vm))
            {
                Func<bool>? readState = handler.HandlerType switch
                {
                    HandlerType.StaticVerb => () => ReadStaticVerbRegistryState(registryService, handler),
                    HandlerType.ModernPackaged or HandlerType.DragDropHandler => null,
                    _ => () => ReadHandlerRegistryState(registryService, handler),
                };
                vm = new ContextMenuHandlerViewModel(handler, pendingChangesService, readState);
                vmMap[key] = vm;
                vmTabs[key] = [];
                vmScopes[key] = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            }

            // Assign to tabs based on all scopes
            foreach (var scope in handler.AllScopes ?? [handler.AppliesTo])
            {
                vmScopes[key].Add(scope);

                var tabs = handler.HandlerType switch
                {
                    HandlerType.StaticVerb => ContextMenuTabMapper.GetTabsForStaticVerbScope(scope),
                    HandlerType.ModernPackaged => ContextMenuTabMapper.GetTabsForModernScope(scope),
                    HandlerType.DragDropHandler => [ContextMenuTab.Misc],
                    _ => ContextMenuTabMapper.GetTabs(scope, handler.VisibleSurfaces),
                };
                foreach (var tab in tabs)
                    vmTabs[key].Add(tab);

                // Set misc group if applicable
                var miscGroup = ContextMenuTabMapper.GetMiscGroup(scope);
                if (miscGroup is not null)
                    vm.MiscGroup = miscGroup;
            }
        }

        // Detect inactive handlers based on system state
        var inactiveDetector = new InactiveHandlerDetector(registryService);
        foreach (var vm in vmMap.Values)
        {
            var (isInactive, reason) = inactiveDetector.Check(vm);
            vm.IsInactive = isInactive;
            vm.InactiveReason = reason;
        }

        // Route based on distinct registry scopes, not UI tab count.
        // A handler at "Folder background" maps to both FolderBackground + Desktop tabs
        // but that's one scope — it stays in those tabs, not Multi.
        foreach (var (key, vm) in vmMap)
        {
            var originalTabs = vmTabs[key];
            var distinctScopes = vmScopes[key].Count;

            if (distinctScopes > 1)
            {
                // Genuinely multi-scope: assign scope badges and route to Multi tab only
                vm.ScopeBadges = BuildScopeBadges(originalTabs);
                var multiTabs = new HashSet<ContextMenuTab> { ContextMenuTab.Multi };
                _allHandlerEntries.Add((vm, multiTabs));
            }
            else
            {
                _allHandlerEntries.Add((vm, originalTabs));
            }
        }

        PopulateCollections();
    }

    private void PopulateCollections()
    {
        Dispatcher.UIThread.VerifyAccess();

        FileHandlers.Clear();
        FolderHandlers.Clear();
        FolderBackgroundHandlers.Clear();
        DesktopHandlers.Clear();
        MiscHandlers.Clear();
        MultiHandlers.Clear();
        DriveMiscHandlers.Clear();
        ThisPcMiscHandlers.Clear();
        NetworkMiscHandlers.Clear();
        RecycleBinMiscHandlers.Clear();

        var currentTypeFilter = HandlerTypeFilter;
        var orphanFilterActive = IsOrphanFilterActive;
        var orphanTotal = 0;

        // Accumulate into temp lists to avoid sort notification storm
        var tmpFile = new List<ContextMenuHandlerViewModel>();
        var tmpFolder = new List<ContextMenuHandlerViewModel>();
        var tmpFolderBg = new List<ContextMenuHandlerViewModel>();
        var tmpDesktop = new List<ContextMenuHandlerViewModel>();
        var tmpMulti = new List<ContextMenuHandlerViewModel>();
        var tmpMisc = new List<ContextMenuHandlerViewModel>();

        foreach (var (vm, tabs) in _allHandlerEntries)
        {
            if (currentTypeFilter is not null && vm.HandlerType != currentTypeFilter)
                continue;
            if (orphanFilterActive && !vm.IsOrphaned)
                continue;

            if (vm.IsOrphaned)
                orphanTotal++;

            if (tabs.Contains(ContextMenuTab.Multi))
            {
                tmpMulti.Add(vm);
                continue;
            }

            if (tabs.Contains(ContextMenuTab.File))
                tmpFile.Add(vm);
            if (tabs.Contains(ContextMenuTab.Folder))
                tmpFolder.Add(vm);
            if (tabs.Contains(ContextMenuTab.FolderBackground))
                tmpFolderBg.Add(vm);
            if (tabs.Contains(ContextMenuTab.Desktop))
                tmpDesktop.Add(vm);
            if (tabs.Contains(ContextMenuTab.Misc))
            {
                tmpMisc.Add(vm);

                var collection = vm.MiscGroup switch
                {
                    MiscSurfaceGroup.Drive => DriveMiscHandlers,
                    MiscSurfaceGroup.ThisPc => ThisPcMiscHandlers,
                    MiscSurfaceGroup.Network => NetworkMiscHandlers,
                    MiscSurfaceGroup.RecycleBin => RecycleBinMiscHandlers,
                    _ => null,
                };
                collection?.Add(vm);
            }
        }

        // Add to observable collections sorted: active first, inactive last
        AddSortedByActive(FileHandlers, tmpFile);
        AddSortedByActive(FolderHandlers, tmpFolder);
        AddSortedByActive(FolderBackgroundHandlers, tmpFolderBg);
        AddSortedByActive(DesktopHandlers, tmpDesktop);
        AddSortedByActive(MultiHandlers, tmpMulti);
        AddSortedByActive(MiscHandlers, tmpMisc);

        // Count orphans respecting the handler type filter
        if (!orphanFilterActive && currentTypeFilter is null)
            OrphanCount = _allHandlerEntries.Count(e => e.Vm.IsOrphaned);
        else if (!orphanFilterActive)
            OrphanCount = _allHandlerEntries.Count(e => e.Vm.IsOrphaned && e.Vm.HandlerType == currentTypeFilter);
        else
            OrphanCount = orphanTotal;

        // Compute summary counts respecting handler type filter
        var filtered = currentTypeFilter is null
            ? _allHandlerEntries
            : _allHandlerEntries.Where(e => e.Vm.HandlerType == currentTypeFilter).ToList();

        ComHandlerCount = filtered.Count(e => e.Vm.HandlerType == HandlerType.ComHandler);
        StaticVerbCount = filtered.Count(e => e.Vm.HandlerType == HandlerType.StaticVerb);
        ModernPackagedCount = filtered.Count(e => e.Vm.HandlerType == HandlerType.ModernPackaged);
        DualRegisteredCount = filtered.Count(e => e.Vm.IsDualRegistered);
        DragDropHandlerCount = filtered.Count(e => e.Vm.HandlerType == HandlerType.DragDropHandler);

        OnPropertyChanged(nameof(FileHandlerCount));
        OnPropertyChanged(nameof(FolderHandlerCount));
        OnPropertyChanged(nameof(FolderBackgroundHandlerCount));
        OnPropertyChanged(nameof(DesktopHandlerCount));
        OnPropertyChanged(nameof(MiscHandlerCount));
        OnPropertyChanged(nameof(MultiHandlerCount));
        OnPropertyChanged(nameof(ScanSummary));
    }

    partial void OnSelectedExtensionChanged(string? value)
    {
        // Fire-and-forget async scan (CommunityToolkit.Mvvm doesn't support async partial)
        _ = ScanFileTypeAsync(value);
    }

    private async Task ScanFileTypeAsync(string? extension)
    {
        // Dispose old per-file-type VMs
        foreach (var vm in PerFileTypeHandlers)
            vm.Dispose();
        PerFileTypeHandlers.Clear();

        if (string.IsNullOrEmpty(extension) || _scanner is null)
            return;

        IsFileTypeScanning = true;
        try
        {
            // Offload registry reads to thread pool
            var handlers = await Task.Run(() => _scanner.ScanFileType(extension)).ConfigureAwait(true);
            var inactiveDetector = new InactiveHandlerDetector(_registryService);
            var tmpFileType = new List<ContextMenuHandlerViewModel>();

            foreach (var handler in handlers)
            {
                Func<bool>? readState = handler.HandlerType switch
                {
                    HandlerType.StaticVerb => () => ReadStaticVerbRegistryState(_registryService, handler),
                    _ => () => ReadHandlerRegistryState(_registryService, handler),
                };
                var vm = new ContextMenuHandlerViewModel(handler, _pendingChangesService, readState);
                var (isInactive, reason) = inactiveDetector.Check(vm);
                vm.IsInactive = isInactive;
                vm.InactiveReason = reason;
                tmpFileType.Add(vm);
            }

            AddSortedByActive(PerFileTypeHandlers, tmpFileType);
        }
        finally
        {
            IsFileTypeScanning = false;
        }
    }

    partial void OnHandlerTypeFilterChanged(HandlerType? value) => PopulateCollections();
    partial void OnIsOrphanFilterActiveChanged(bool value) => PopulateCollections();
    partial void OnOrphanCountChanged(int value) => CleanUpAllOrphansCommand.NotifyCanExecuteChanged();

    [RelayCommand]
    private void ToggleOrphanFilter() => IsOrphanFilterActive = !IsOrphanFilterActive;

    [RelayCommand(CanExecute = nameof(CanCleanUpAllOrphans))]
    private void CleanUpAllOrphans()
    {
        // Deduplicate VMs (same VM can appear in multiple tabs)
        // Respect handler type filter: only clean orphans visible in the current view
        var currentTypeFilter = HandlerTypeFilter;
        var seen = new HashSet<ContextMenuHandlerViewModel>(ReferenceEqualityComparer.Instance);
        var orphanHandlers = new List<ContextMenuHandler>();

        foreach (var (vm, _) in _allHandlerEntries)
        {
            if (currentTypeFilter is not null && vm.HandlerType != currentTypeFilter)
                continue;
            if (!vm.IsOrphaned || !seen.Add(vm))
                continue;

            orphanHandlers.Add(vm.Handler);
        }

        if (orphanHandlers.Count == 0)
            return;

        var bulkGroup = ContextMenuChangeFactory.CreateBulkOrphanCleanup(orphanHandlers);
        _pendingChangesService.Stage(bulkGroup);
    }

    private bool CanCleanUpAllOrphans() => OrphanCount > 0;

    private static void AddSortedByActive(
        ObservableCollection<ContextMenuHandlerViewModel> collection,
        List<ContextMenuHandlerViewModel> items)
    {
        // Add active first, then inactive — avoids clear+re-add notification storm
        foreach (var vm in items.Where(v => !v.IsInactive))
            collection.Add(vm);
        foreach (var vm in items.Where(v => v.IsInactive))
            collection.Add(vm);
    }

    private static IReadOnlyList<ScopeBadge> BuildScopeBadges(HashSet<ContextMenuTab> tabs)
    {
        var badges = new List<ScopeBadge>();
        if (tabs.Contains(ContextMenuTab.File))
            badges.Add(new ScopeBadge("Files", "ScopeBadgeFileBrush"));
        if (tabs.Contains(ContextMenuTab.Folder))
            badges.Add(new ScopeBadge("Folders", "ScopeBadgeFolderBrush"));
        if (tabs.Contains(ContextMenuTab.FolderBackground))
            badges.Add(new ScopeBadge("Background", "ScopeBadgeBackgroundBrush"));
        if (tabs.Contains(ContextMenuTab.Desktop))
            badges.Add(new ScopeBadge("Desktop", "ScopeBadgeDesktopBrush"));
        if (tabs.Contains(ContextMenuTab.Misc))
            badges.Add(new ScopeBadge("Misc", "ScopeBadgeMiscBrush"));
        return badges;
    }

    private static string MakeHandlerKey(ContextMenuHandler handler)
    {
        if (handler.HandlerType == HandlerType.StaticVerb)
        {
            var exec = handler.VerbInfo?.CommandLine ?? handler.VerbInfo?.DelegateExecuteClsid ?? "no-exec";
            return $"verb|{handler.VerbInfo?.VerbName ?? handler.Name}|{exec}";
        }
        if (handler.HandlerType == HandlerType.ModernPackaged)
        {
            // Modern handlers keyed by CLSID + package to avoid collisions with COM handlers
            return $"modern|{handler.Clsid}|{handler.PackagedInfo?.PackageFamilyName ?? handler.Name}";
        }
        if (handler.HandlerType == HandlerType.DragDropHandler)
        {
            return $"dragdrop|{handler.Clsid}";
        }
        return handler.Clsid;
    }

    private static bool ReadHandlerRegistryState(IRegistryService registryService, ContextMenuHandler handler)
    {
        // Check blocked list first — if CLSID is in the blocked list, handler is disabled
        var blockedResult = registryService.ValueExists(
            Modules.Shell.ShellRegistryPaths.BlockedListKeyPath, handler.Clsid);
        if (blockedResult.IsSuccess && blockedResult.Value)
            return false;

        // Check all registry paths -- handler is enabled only if ALL paths have non-dash-prefixed CLSID
        var paths = handler.AllRegistryPaths ?? [handler.RegistryPath];
        foreach (var path in paths)
        {
            var result = registryService.ReadString(path, string.Empty);
            if (!result.IsSuccess)
                return handler.IsEnabled; // fallback to scan value
            if (result.Value!.StartsWith('-'))
                return false;
        }
        return true;
    }

    private static bool ReadStaticVerbRegistryState(IRegistryService registryService, ContextMenuHandler handler)
    {
        // Static verbs: check LegacyDisable at both HKCR path (real Windows merges HKCU)
        // and the HKCU overlay path (where we actually write). This ensures the read-back
        // works both on real Windows (HKCR merge) and in tests (FakeRegistryService, no merge).
        var paths = handler.AllRegistryPaths ?? [handler.RegistryPath];
        foreach (var path in paths)
        {
            var result = registryService.ValueExists(path, "LegacyDisable");
            if (result.IsSuccess && result.Value)
                return false;

            // Also check the HKCU overlay where CreateStaticVerbToggle writes
            var hkcuPath = Modules.Shell.ShellRegistryPaths.RemapHkcrToHkcu(path);
            if (hkcuPath != path)
            {
                var hkcuResult = registryService.ValueExists(hkcuPath, "LegacyDisable");
                if (hkcuResult.IsSuccess && hkcuResult.Value)
                    return false;
            }
        }
        return true;
    }

    public void Dispose()
    {
        // Dispose all handler VMs including any filtered out of visible collections
        var disposed = new HashSet<ContextMenuHandlerViewModel>(ReferenceEqualityComparer.Instance);
        foreach (var (vm, _) in _allHandlerEntries)
        {
            if (disposed.Add(vm))
                vm.Dispose();
        }
        // Also dispose per-file-type VMs (not in _allHandlerEntries)
        foreach (var vm in PerFileTypeHandlers)
        {
            if (disposed.Add(vm))
                vm.Dispose();
        }
    }

    partial void OnIsRegistryViewModeChanged(bool value)
    {
        // Deduplicate: shared VM instances appear in multiple tab collections
        var seen = new HashSet<ContextMenuHandlerViewModel>(ReferenceEqualityComparer.Instance);
        foreach (var collection in (ObservableCollection<ContextMenuHandlerViewModel>[])
            [FileHandlers, FolderHandlers, FolderBackgroundHandlers, DesktopHandlers, MiscHandlers, MultiHandlers, PerFileTypeHandlers])
        {
            foreach (var vm in collection)
            {
                if (seen.Add(vm))
                    vm.SetRegistryViewMode(value);
            }
        }
    }
}
