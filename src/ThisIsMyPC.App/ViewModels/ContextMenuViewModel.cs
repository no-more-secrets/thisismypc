using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using ThisIsMyPC.Core.Services;
using ThisIsMyPC.Modules.Shell.Changes;
using ThisIsMyPC.Modules.Shell.Models;

namespace ThisIsMyPC.App.ViewModels;

public partial class ContextMenuViewModel : ViewModelBase
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
        // One VM per unique CLSID (already deduped by scanner)
        var vmMap = new Dictionary<string, ContextMenuHandlerViewModel>(StringComparer.OrdinalIgnoreCase);
        // Track which tabs each VM belongs to
        var vmTabs = new Dictionary<string, HashSet<ContextMenuTab>>(StringComparer.OrdinalIgnoreCase);

        foreach (var handler in handlers)
        {
            if (!vmMap.TryGetValue(handler.Clsid, out var vm))
            {
                vm = new ContextMenuHandlerViewModel(
                    handler,
                    pendingChangesService,
                    readRegistryState: () => ReadHandlerRegistryState(registryService, handler));
                vmMap[handler.Clsid] = vm;
                vmTabs[handler.Clsid] = [];
            }

            // Assign to tabs based on all scopes
            foreach (var scope in handler.AllScopes ?? [handler.AppliesTo])
            {
                var tabs = ContextMenuTabMapper.GetTabs(scope, handler.VisibleSurfaces);
                foreach (var tab in tabs)
                    vmTabs[handler.Clsid].Add(tab);

                // Set misc group if applicable
                var miscGroup = ContextMenuTabMapper.GetMiscGroup(scope);
                if (miscGroup is not null)
                    vm.MiscGroup = miscGroup;
            }
        }

        // Populate tab collections with shared VM instances
        foreach (var (clsid, vm) in vmMap)
        {
            var tabs = vmTabs[clsid];

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

                // Sub-group by surface
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
        foreach (var (clsid, vm) in vmMap)
        {
            var tabs = vmTabs[clsid];
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

    private static bool ReadHandlerRegistryState(IRegistryService registryService, ContextMenuHandler handler)
    {
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
