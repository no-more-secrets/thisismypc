using ThisIsMyPC.Interop.Com.Shell;
using ThisIsMyPC.Interop.Win32.Registry;
using ThisIsMyPC.Modules.Shell.Models;

namespace ThisIsMyPC.Modules.Shell.Services;

public sealed class ContextMenuScanner
{
    private readonly IShellExtensionService _shellExtensionService;
    private readonly IContextMenuProbe? _contextMenuProbe;
    private readonly IStaticVerbService? _staticVerbService;

    public ContextMenuScanner(
        IShellExtensionService shellExtensionService,
        IContextMenuProbe? contextMenuProbe = null,
        IStaticVerbService? staticVerbService = null)
    {
        _shellExtensionService = shellExtensionService;
        _contextMenuProbe = contextMenuProbe;
        _staticVerbService = staticVerbService;
    }

    public IReadOnlyList<ContextMenuHandler> Scan()
    {
        var handlers = new List<ContextMenuHandler>();

        // COM handlers
        handlers.AddRange(ScanComHandlers());

        // Static verbs
        if (_staticVerbService is not null)
            handlers.AddRange(ScanStaticVerbs());

        return handlers;
    }

    private IReadOnlyList<ContextMenuHandler> ScanComHandlers()
    {
        var result = _shellExtensionService.EnumerateContextMenuHandlers();
        if (!result.IsSuccess)
            return [];

        var blockedClsids = _shellExtensionService.GetBlockedClsids();

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

            // Track per-path enabled state for accurate ChangeDescriptor BeforeValue
            var pathEnabledStates = entries.ToDictionary(
                e => e.RegistryPath,
                e => e.IsEnabled,
                StringComparer.OrdinalIgnoreCase);

            // Determine disable method
            var hasDashPrefix = entries.Any(e => !e.IsEnabled);
            var isBlockedList = blockedClsids.Contains(first.Clsid);
            var disableMethod = (hasDashPrefix, isBlockedList) switch
            {
                (true, true) => DisableMethod.Both,
                (true, false) => DisableMethod.DashPrefix,
                (false, true) => DisableMethod.BlockedList,
                _ => DisableMethod.None,
            };

            // IsEnabled is true only if ALL registration entries are enabled AND not blocked
            var isEnabled = entries.All(e => e.IsEnabled) && !isBlockedList;

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
                VisibleSurfaces: visibleSurfaces,
                DisableMethod: disableMethod,
                HandlerType: HandlerType.ComHandler));
        }

        return handlers;
    }

    private IReadOnlyList<ContextMenuHandler> ScanStaticVerbs()
    {
        var result = _staticVerbService!.EnumerateStaticVerbs();
        if (!result.IsSuccess)
            return [];

        // Group by verb name + command (case-insensitive) for deduplication
        // Same verb at multiple scope levels = same logical verb
        var grouped = result.Value!
            .GroupBy(e => MakeVerbDeduplicationKey(e), StringComparer.OrdinalIgnoreCase);

        var handlers = new List<ContextMenuHandler>();

        foreach (var group in grouped)
        {
            var entries = group.ToList();
            var first = entries[0];

            var allRegistryPaths = entries.Select(e => e.RegistryPath).ToList();
            var allScopes = entries.Select(e => e.Scope).Distinct(StringComparer.OrdinalIgnoreCase).ToList();

            // Per-path LegacyDisable state tracking
            var pathEnabledStates = entries.ToDictionary(
                e => e.RegistryPath,
                e => !e.IsLegacyDisabled,
                StringComparer.OrdinalIgnoreCase);

            // IsEnabled is true only if no path has LegacyDisable
            var isEnabled = entries.All(e => !e.IsLegacyDisabled);

            var displayName = first.MuiVerb ?? first.VerbName;
            var classification = ContextMenuHandlerClassifier.ClassifyStaticVerb(
                first.VerbName, first.CommandLine);

            var verbInfo = new StaticVerbInfo(
                VerbName: first.VerbName,
                MuiVerb: first.MuiVerb,
                Icon: first.Icon,
                Position: first.Position,
                IsExtended: first.IsExtended,
                CommandLine: first.CommandLine,
                DelegateExecuteClsid: first.DelegateExecuteClsid,
                IsLegacyDisabled: !isEnabled,
                AppliesTo: first.AppliesTo,
                HasLuaShield: first.HasLuaShield,
                IsProgrammaticAccessOnly: first.IsProgrammaticAccessOnly);

            handlers.Add(new ContextMenuHandler(
                Name: displayName,
                Clsid: string.Empty,
                RegistryPath: first.RegistryPath,
                AppliesTo: first.Scope,
                DllPath: null,
                Publisher: null,
                IsEnabled: isEnabled,
                Classification: classification,
                AllRegistryPaths: allRegistryPaths,
                AllScopes: allScopes,
                PathEnabledStates: pathEnabledStates,
                HandlerType: HandlerType.StaticVerb,
                VerbInfo: verbInfo));
        }

        return handlers;
    }

    private static string MakeVerbDeduplicationKey(StaticVerbEntry entry)
    {
        // Deduplicate by verb name + command path (or DelegateExecute CLSID)
        var executionKey = entry.CommandLine ?? entry.DelegateExecuteClsid ?? "no-exec";
        return $"{entry.VerbName}|{executionKey}";
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
