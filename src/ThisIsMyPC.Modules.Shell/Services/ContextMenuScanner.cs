using ThisIsMyPC.Interop.Com.Shell;
using ThisIsMyPC.Interop.Win32.Registry;
using ThisIsMyPC.Modules.Shell.Models;

namespace ThisIsMyPC.Modules.Shell.Services;

public sealed class ContextMenuScanner
{
    private readonly IShellExtensionService _shellExtensionService;
    private readonly IContextMenuProbe? _contextMenuProbe;
    private readonly IStaticVerbService? _staticVerbService;
    private readonly IModernPackagedHandlerService? _modernPackagedService;
    private readonly ProgIdResolver? _progIdResolver;
    private readonly FileTypeVerbService? _fileTypeVerbService;

    public ContextMenuScanner(
        IShellExtensionService shellExtensionService,
        IContextMenuProbe? contextMenuProbe = null,
        IStaticVerbService? staticVerbService = null,
        IModernPackagedHandlerService? modernPackagedService = null,
        ProgIdResolver? progIdResolver = null,
        FileTypeVerbService? fileTypeVerbService = null)
    {
        _shellExtensionService = shellExtensionService;
        _contextMenuProbe = contextMenuProbe;
        _staticVerbService = staticVerbService;
        _modernPackagedService = modernPackagedService;
        _progIdResolver = progIdResolver;
        _fileTypeVerbService = fileTypeVerbService;
    }

    public IReadOnlyList<ContextMenuHandler> Scan()
    {
        var handlers = new List<ContextMenuHandler>();

        // COM handlers
        var comHandlers = ScanComHandlers();
        var comWithOrphans = DetectOrphans(comHandlers);
        handlers.AddRange(comWithOrphans);

        // Static verbs
        if (_staticVerbService is not null)
            handlers.AddRange(ScanStaticVerbs());

        // Drag-drop handlers
        handlers.AddRange(ScanDragDropHandlers());

        // Modern packaged handlers
        if (_modernPackagedService is not null)
        {
            var modernHandlers = ScanModernPackaged();
            handlers.AddRange(modernHandlers);

            // Cross-type deduplication: detect same-CLSID COM+Modern pairs
            ApplyCrossTypeDeduplication(handlers, comWithOrphans, modernHandlers);
        }

        // Remove internal shell verbs that never produce visible menu entries
        handlers.RemoveAll(InternalHandlerFilter.ShouldHide);

        return handlers;
    }

    /// <summary>
    /// Scans a specific file extension's ProgID chain for static verbs and COM handlers.
    /// Returns handlers ready for display in the Per File Type tab.
    /// </summary>
    public IReadOnlyList<ContextMenuHandler> ScanFileType(string extension)
    {
        if (_progIdResolver is null || _fileTypeVerbService is null)
            return [];

        var resolveResult = _progIdResolver.Resolve(extension);
        if (!resolveResult.IsSuccess)
            return [];

        var progIdEntries = resolveResult.Value!;
        var handlers = new List<ContextMenuHandler>();

        // Static verbs from ProgID chain
        var verbsResult = _fileTypeVerbService.ScanVerbs(progIdEntries);
        if (verbsResult.IsSuccess)
        {
            foreach (var entry in verbsResult.Value!)
            {
                var classification = ContextMenuHandlerClassifier.ClassifyStaticVerb(
                    entry.VerbName, entry.CommandLine, entry.DelegateExecuteClsid);

                var displayName = entry.MuiVerb ?? entry.VerbName;

                var verbInfo = new StaticVerbInfo(
                    VerbName: entry.VerbName,
                    MuiVerb: entry.MuiVerb,
                    Icon: entry.Icon,
                    Position: entry.Position,
                    IsExtended: entry.IsExtended,
                    CommandLine: entry.CommandLine,
                    DelegateExecuteClsid: entry.DelegateExecuteClsid,
                    IsLegacyDisabled: entry.IsLegacyDisabled,
                    AppliesTo: entry.AppliesTo,
                    HasLuaShield: entry.HasLuaShield,
                    IsProgrammaticAccessOnly: entry.IsProgrammaticAccessOnly);

                handlers.Add(new ContextMenuHandler(
                    Name: displayName,
                    Clsid: string.Empty,
                    RegistryPath: entry.RegistryPath,
                    AppliesTo: entry.Scope,
                    DllPath: null,
                    Publisher: null,
                    IsEnabled: !entry.IsLegacyDisabled,
                    Classification: classification,
                    AllRegistryPaths: [entry.RegistryPath],
                    AllScopes: [entry.Scope],
                    PathEnabledStates: new Dictionary<string, bool> { [entry.RegistryPath] = !entry.IsLegacyDisabled },
                    HandlerType: HandlerType.StaticVerb,
                    VerbInfo: verbInfo));
            }
        }

        // COM handlers from ProgID chain
        var comResult = _fileTypeVerbService.ScanComHandlers(progIdEntries);
        if (comResult.IsSuccess)
        {
            var blockedClsids = _shellExtensionService.GetBlockedClsids();

            foreach (var comHandler in comResult.Value!)
            {
                var classification = ContextMenuHandlerClassifier.Classify(
                    comHandler.Clsid, comHandler.DllPath, null);
                var isBlockedList = blockedClsids.Contains(comHandler.Clsid);
                var isEnabled = comHandler.IsEnabled && !isBlockedList;

                var displayName = KnownHandlerDisplayNames.GetDisplayName(comHandler.Clsid) ?? comHandler.Name;

                handlers.Add(new ContextMenuHandler(
                    Name: displayName,
                    Clsid: comHandler.Clsid,
                    RegistryPath: comHandler.RegistryPath,
                    AppliesTo: comHandler.Scope,
                    DllPath: comHandler.DllPath,
                    Publisher: null,
                    IsEnabled: isEnabled,
                    Classification: classification,
                    AllRegistryPaths: [comHandler.RegistryPath],
                    AllScopes: [comHandler.Scope],
                    HandlerType: HandlerType.ComHandler,
                    DisableMethod: isBlockedList ? DisableMethod.BlockedList : DisableMethod.None));
            }
        }

        // Filter internal verbs
        handlers.RemoveAll(InternalHandlerFilter.ShouldHide);

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

            // Tag handlers registered under SystemFileAssociations\Directory.Audio|Video
            var isContentInspecting = allScopes.Any(s =>
                s.Equals("Audio folders", StringComparison.OrdinalIgnoreCase) ||
                s.Equals("Video folders", StringComparison.OrdinalIgnoreCase));

            var displayName = KnownHandlerDisplayNames.GetDisplayName(first.Clsid) ?? first.HandlerName;

            handlers.Add(new ContextMenuHandler(
                Name: displayName,
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
                HandlerType: HandlerType.ComHandler,
                RegistryKeyName: first.EffectiveRegistryKeyName,
                IsContentInspecting: isContentInspecting));
        }

        return handlers;
    }

    private IReadOnlyList<ContextMenuHandler> ScanDragDropHandlers()
    {
        var result = _shellExtensionService.EnumerateDragDropHandlers();
        if (!result.IsSuccess)
            return [];

        // Deduplicate by CLSID across the 3 registration paths
        var grouped = result.Value!
            .GroupBy(info => info.Clsid, StringComparer.OrdinalIgnoreCase);

        var handlers = new List<ContextMenuHandler>();

        foreach (var group in grouped)
        {
            var entries = group.ToList();
            var first = entries[0];
            var allRegistryPaths = entries.Select(e => e.RegistryPath).ToList();
            var allScopes = entries.Select(e => e.AppliesTo).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
            var pathEnabledStates = entries.ToDictionary(
                e => e.RegistryPath,
                e => true,
                StringComparer.OrdinalIgnoreCase);

            handlers.Add(new ContextMenuHandler(
                Name: first.Name,
                Clsid: first.Clsid,
                RegistryPath: first.RegistryPath,
                AppliesTo: first.AppliesTo,
                DllPath: first.DllPath,
                Publisher: first.Publisher,
                IsEnabled: true,
                Classification: ContextMenuHandlerClassifier.Classify(first.Clsid, first.DllPath, first.Publisher),
                AllRegistryPaths: allRegistryPaths,
                AllScopes: allScopes,
                PathEnabledStates: pathEnabledStates,
                HandlerType: HandlerType.DragDropHandler,
                RegistryKeyName: first.RegistryKeyName));
        }

        return handlers;
    }

    private static IReadOnlyList<ContextMenuHandler> DetectOrphans(IReadOnlyList<ContextMenuHandler> comHandlers)
    {
        var result = new List<ContextMenuHandler>(comHandlers.Count);

        foreach (var handler in comHandlers)
        {
            if (handler.DllPath is null)
            {
                // No InProcServer32 DLL path; CLSID registered in shellex but not in HKCR\CLSID
                result.Add(handler with
                {
                    IsOrphaned = true,
                    OrphanReason = $"CLSID {handler.Clsid} not registered (no HKCR\\CLSID entry)",
                });
                continue;
            }

            // Expand environment variables and strip surrounding quotes before existence check
            var expandedPath = Environment.ExpandEnvironmentVariables(handler.DllPath);
            if (expandedPath.Length >= 2 && expandedPath[0] == '"' && expandedPath[^1] == '"')
                expandedPath = expandedPath[1..^1];

            if (!File.Exists(expandedPath))
            {
                result.Add(handler with
                {
                    IsOrphaned = true,
                    OrphanReason = $"DLL not found: {handler.DllPath}",
                });
            }
            else
            {
                result.Add(handler);
            }
        }

        return result;
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
                first.VerbName, first.CommandLine, first.DelegateExecuteClsid);

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

    private IReadOnlyList<ContextMenuHandler> ScanModernPackaged()
    {
        var result = _modernPackagedService!.EnumerateModernHandlers();
        if (!result.IsSuccess)
            return [];

        var handlers = new List<ContextMenuHandler>();

        foreach (var entry in result.Value!)
        {
            var allScopes = MapItemTypesToScopes(entry.ItemTypes);
            var classification = ContextMenuHandlerClassifier.ClassifyModernPackaged(
                entry.PackageFamilyName, entry.PublisherDisplayName);

            var packagedInfo = new ModernPackagedInfo(
                PackageFamilyName: entry.PackageFamilyName,
                PackageDisplayName: entry.PackageDisplayName,
                PublisherDisplayName: entry.PublisherDisplayName,
                ItemTypes: entry.ItemTypes,
                VerbId: entry.VerbId,
                InstallSource: entry.InstallSource);

            handlers.Add(new ContextMenuHandler(
                Name: entry.HandlerName,
                Clsid: entry.Clsid,
                RegistryPath: $"PackagedCom\\{entry.PackageFamilyName}\\{entry.Clsid}",
                AppliesTo: allScopes.Count > 0 ? allScopes[0] : "Unknown scope",
                DllPath: null,
                Publisher: entry.PublisherDisplayName,
                IsEnabled: true,
                Classification: classification,
                AllScopes: allScopes,
                DisableMethod: DisableMethod.None,
                HandlerType: HandlerType.ModernPackaged,
                PackagedInfo: packagedInfo));
        }

        return handlers;
    }

    private static List<string> MapItemTypesToScopes(IReadOnlyList<string>? itemTypes)
    {
        if (itemTypes is null || itemTypes.Count == 0)
            return ["Unknown scope"];

        var scopes = new List<string>();
        foreach (var itemType in itemTypes)
        {
            var scope = itemType switch
            {
                "*" => "All files",
                "Directory" => "Directories",
                @"Directory\Background" => "Folder background",
                _ when itemType.StartsWith('.') => "All files", // Extension-specific → File tab
                _ => "Unknown scope",
            };

            if (!scopes.Contains(scope, StringComparer.OrdinalIgnoreCase))
                scopes.Add(scope);
        }

        return scopes;
    }

    private static void ApplyCrossTypeDeduplication(
        List<ContextMenuHandler> allHandlers,
        IReadOnlyList<ContextMenuHandler> comHandlers,
        IReadOnlyList<ContextMenuHandler> modernHandlers)
    {
        // Build CLSID lookup from COM handlers
        var comByClsid = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        for (var i = 0; i < allHandlers.Count; i++)
        {
            if (allHandlers[i].HandlerType == HandlerType.ComHandler &&
                !string.IsNullOrEmpty(allHandlers[i].Clsid))
            {
                comByClsid.TryAdd(allHandlers[i].Clsid, i);
            }
        }

        // Check each modern handler for CLSID match
        for (var i = 0; i < allHandlers.Count; i++)
        {
            var handler = allHandlers[i];
            if (handler.HandlerType != HandlerType.ModernPackaged ||
                string.IsNullOrEmpty(handler.Clsid))
                continue;

            if (comByClsid.TryGetValue(handler.Clsid, out var comIndex))
            {
                var comHandler = allHandlers[comIndex];

                // Mark both handlers as dual-registered
                allHandlers[comIndex] = comHandler with
                {
                    IsDualRegistered = true,
                    DualRegistrationPartnerName = handler.Name,
                };

                allHandlers[i] = handler with
                {
                    IsDualRegistered = true,
                    DualRegistrationPartnerName = comHandler.Name,
                };
            }
        }
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
