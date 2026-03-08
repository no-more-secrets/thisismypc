using ThisIsMyPC.Interop.Com.Shell;
using ThisIsMyPC.Modules.Shell.Models;

namespace ThisIsMyPC.Modules.Shell.Services;

public sealed class ContextMenuScanner
{
    private readonly IShellExtensionService _shellExtensionService;

    public ContextMenuScanner(IShellExtensionService shellExtensionService)
    {
        _shellExtensionService = shellExtensionService;
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

            var classification = ContextMenuHandlerClassifier.Classify(first.Clsid, first.DllPath, first.Publisher);

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
                AllScopes: allScopes));
        }

        return handlers;
    }
}
