using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using ThisIsMyPC.Core.Services;
using ThisIsMyPC.Modules.Shell.Changes;
using ThisIsMyPC.Modules.Shell.Models;

namespace ThisIsMyPC.App.ViewModels;

public partial class ContextMenuViewModel : ViewModelBase, IDisposable
{
    public ObservableCollection<ContextMenuHandlerViewModel> FileHandlers { get; } = [];
    public ObservableCollection<ContextMenuHandlerViewModel> FolderHandlers { get; } = [];
    public ObservableCollection<ContextMenuHandlerViewModel> FolderBackgroundHandlers { get; } = [];
    public ObservableCollection<ContextMenuHandlerViewModel> DesktopHandlers { get; } = [];
    public ObservableCollection<ContextMenuHandlerViewModel> MiscHandlers { get; } = [];

    // Misc tab sub-groups by surface
    public ObservableCollection<ContextMenuHandlerViewModel> DriveMiscHandlers { get; } = [];
    public ObservableCollection<ContextMenuHandlerViewModel> ThisPcMiscHandlers { get; } = [];
    public ObservableCollection<ContextMenuHandlerViewModel> NetworkMiscHandlers { get; } = [];
    public ObservableCollection<ContextMenuHandlerViewModel> RecycleBinMiscHandlers { get; } = [];

    [ObservableProperty]
    private bool _isRegistryViewMode;

    [ObservableProperty]
    private HandlerType? _handlerTypeFilter;

    public string FileHandlerCount => $"File ({FileHandlers.Count})";
    public string FolderHandlerCount => $"Folder ({FolderHandlers.Count})";
    public string FolderBackgroundHandlerCount => $"Folder Background ({FolderBackgroundHandlers.Count})";
    public string DesktopHandlerCount => $"Desktop ({DesktopHandlers.Count})";
    public string MiscHandlerCount => $"Misc ({MiscHandlers.Count})";

    public ContextMenuViewModel(
        IReadOnlyList<ContextMenuHandler> handlers,
        IPendingChangesService pendingChangesService,
        IRegistryService registryService)
    {
        BuildContextMenuHandlerTabs(handlers, pendingChangesService, registryService);
    }

    private void BuildContextMenuHandlerTabs(
        IReadOnlyList<ContextMenuHandler> handlers,
        IPendingChangesService pendingChangesService,
        IRegistryService registryService)
    {
        // Unique key: CLSID for COM handlers, verb dedup key for static verbs
        var vmMap = new Dictionary<string, ContextMenuHandlerViewModel>(StringComparer.OrdinalIgnoreCase);
        var vmTabs = new Dictionary<string, HashSet<ContextMenuTab>>(StringComparer.OrdinalIgnoreCase);

        foreach (var handler in handlers)
        {
            var key = MakeHandlerKey(handler);

            if (!vmMap.TryGetValue(key, out var vm))
            {
                vm = new ContextMenuHandlerViewModel(
                    handler,
                    pendingChangesService,
                    readRegistryState: handler.HandlerType == HandlerType.StaticVerb
                        ? () => ReadStaticVerbRegistryState(registryService, handler)
                        : () => ReadHandlerRegistryState(registryService, handler));
                vmMap[key] = vm;
                vmTabs[key] = [];
            }

            // Assign to tabs based on all scopes
            foreach (var scope in handler.AllScopes ?? [handler.AppliesTo])
            {
                var tabs = handler.HandlerType == HandlerType.StaticVerb
                    ? ContextMenuTabMapper.GetTabsForStaticVerbScope(scope)
                    : ContextMenuTabMapper.GetTabs(scope, handler.VisibleSurfaces);
                foreach (var tab in tabs)
                    vmTabs[key].Add(tab);

                // Set misc group if applicable
                var miscGroup = ContextMenuTabMapper.GetMiscGroup(scope);
                if (miscGroup is not null)
                    vm.MiscGroup = miscGroup;
            }
        }

        // Populate tab collections with shared VM instances
        foreach (var (key, vm) in vmMap)
        {
            var tabs = vmTabs[key];

            if (tabs.Contains(ContextMenuTab.File))
                FileHandlers.Add(vm);
            if (tabs.Contains(ContextMenuTab.Folder))
                FolderHandlers.Add(vm);
            if (tabs.Contains(ContextMenuTab.FolderBackground))
                FolderBackgroundHandlers.Add(vm);
            if (tabs.Contains(ContextMenuTab.Desktop))
                DesktopHandlers.Add(vm);
            if (tabs.Contains(ContextMenuTab.Misc))
            {
                MiscHandlers.Add(vm);

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

        // Generate ScopeNote for each VM based on which tabs it appears in
        foreach (var (key, vm) in vmMap)
        {
            var tabs = vmTabs[key];
            if (tabs.Count <= 1)
                continue;

            var tabNames = tabs.Select(t => t switch
            {
                ContextMenuTab.File => "File",
                ContextMenuTab.Folder => "Folders",
                ContextMenuTab.FolderBackground => "Background",
                ContextMenuTab.Desktop => "Desktop",
                ContextMenuTab.Misc => "Misc",
                _ => t.ToString(),
            });

            vm.SetScopeNote($"appears in: {string.Join(", ", tabNames)}");
        }
    }

    private static string MakeHandlerKey(ContextMenuHandler handler)
    {
        if (handler.HandlerType == HandlerType.StaticVerb)
        {
            var exec = handler.VerbInfo?.CommandLine ?? handler.VerbInfo?.DelegateExecuteClsid ?? "no-exec";
            return $"verb|{handler.VerbInfo?.VerbName ?? handler.Name}|{exec}";
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
        // Static verbs: check LegacyDisable value at each registry path
        var paths = handler.AllRegistryPaths ?? [handler.RegistryPath];
        foreach (var path in paths)
        {
            var result = registryService.ValueExists(path, "LegacyDisable");
            if (result.IsSuccess && result.Value)
                return false; // LegacyDisable exists = disabled
        }
        return true;
    }

    public void Dispose()
    {
        // Dispose all child handler VMs to unsubscribe from PendingChangesService
        var disposed = new HashSet<ContextMenuHandlerViewModel>(ReferenceEqualityComparer.Instance);
        foreach (var collection in (ObservableCollection<ContextMenuHandlerViewModel>[])
            [FileHandlers, FolderHandlers, FolderBackgroundHandlers, DesktopHandlers, MiscHandlers])
        {
            foreach (var vm in collection)
            {
                if (disposed.Add(vm))
                    vm.Dispose();
            }
        }
    }

    partial void OnIsRegistryViewModeChanged(bool value)
    {
        // Deduplicate: shared VM instances appear in multiple tab collections
        var seen = new HashSet<ContextMenuHandlerViewModel>(ReferenceEqualityComparer.Instance);
        foreach (var collection in (ObservableCollection<ContextMenuHandlerViewModel>[])
            [FileHandlers, FolderHandlers, FolderBackgroundHandlers, DesktopHandlers, MiscHandlers])
        {
            foreach (var vm in collection)
            {
                if (seen.Add(vm))
                    vm.SetRegistryViewMode(value);
            }
        }
    }
}
