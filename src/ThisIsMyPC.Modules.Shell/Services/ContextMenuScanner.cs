using ThisIsMyPC.Interop.Com.Shell;
using ThisIsMyPC.Modules.Shell.Models;

namespace ThisIsMyPC.Modules.Shell.Services;

public sealed class ContextMenuScanner
{
    private readonly IShellExtensionService _shellExtensionService;
    private readonly IContextMenuProbe? _contextMenuProbe;

    public ContextMenuScanner(IShellExtensionService shellExtensionService, IContextMenuProbe? contextMenuProbe = null)
    {
        _shellExtensionService = shellExtensionService;
        _contextMenuProbe = contextMenuProbe;
    }

    public IReadOnlyList<ContextMenuHandler> Scan()
    {
        var result = _shellExtensionService.EnumerateContextMenuHandlers();
        if (!result.IsSuccess)
            return [];

        // Group by CLSID (strip dash prefix for comparison) to deduplicate multi-registration handlers
        var grouped = result.Value!
            .GroupBy(info => info.Clsid, StringComparer.OrdinalIgnoreCase);

        var handlers = new List<ContextMenuHandler>();

        foreach (var group in grouped)
        {
            var entries = group.ToList();
            var first = entries[0];

            // Merge all registry paths and scopes
            var allRegistryPaths = entries.Select(e => e.RegistryPath).ToList();
            var allScopes = entries.Select(e => e.AppliesTo).Distinct(StringComparer.OrdinalIgnoreCase).ToList();

            // IsEnabled is true only if ALL registration entries are enabled (consistent state)
            var isEnabled = entries.All(e => e.IsEnabled);

            // Track per-path enabled state for accurate ChangeDescriptor BeforeValue
            var pathEnabledStates = entries.ToDictionary(
                e => e.RegistryPath,
                e => e.IsEnabled,
                StringComparer.OrdinalIgnoreCase);

            var classification = ContextMenuHandlerClassifier.Classify(first.Clsid, first.DllPath, first.Publisher);

            // Probe surface visibility for background handlers
            var visibleSurfaces = ProbeSurfaceVisibility(first.Clsid, allScopes);

            handlers.Add(new ContextMenuHandler(
                Name: first.HandlerName,
                Clsid: first.Clsid,
                RegistryPath: first.RegistryPath,
                AppliesTo: first.AppliesTo,
                DllPath: first.DllPath,
                Publisher: first.Publisher,
                IsEnabled: isEnabled,
                Classification: classification,
                AllRegistryPaths: allRegistryPaths,
                AllScopes: allScopes,
                PathEnabledStates: pathEnabledStates,
                VisibleSurfaces: visibleSurfaces));
        }

        return handlers;
    }

    private IReadOnlySet<ContextMenuSurface>? ProbeSurfaceVisibility(string clsid, List<string> scopes)
    {
        // Only probe handlers registered under Directory\Background
        if (_contextMenuProbe is null || !scopes.Contains("Folder background", StringComparer.OrdinalIgnoreCase))
            return null;

        var surfaces = new HashSet<ContextMenuSurface>();

        var folderResult = _contextMenuProbe.HandlerAppearsOnSurface(clsid, ContextMenuSurface.FolderBackground);
        if (folderResult.IsSuccess && folderResult.Value)
            surfaces.Add(ContextMenuSurface.FolderBackground);

        var desktopResult = _contextMenuProbe.HandlerAppearsOnSurface(clsid, ContextMenuSurface.DesktopBackground);
        if (desktopResult.IsSuccess && desktopResult.Value)
            surfaces.Add(ContextMenuSurface.DesktopBackground);

        // If handler is also explicitly registered under DesktopBackground path, ensure Desktop is included
        if (scopes.Contains("Desktop background", StringComparer.OrdinalIgnoreCase))
            surfaces.Add(ContextMenuSurface.DesktopBackground);

        return surfaces;
    }
}
