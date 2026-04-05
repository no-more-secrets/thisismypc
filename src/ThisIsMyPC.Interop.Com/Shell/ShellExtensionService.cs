using System.Diagnostics;
using ThisIsMyPC.Core.Results;
using ThisIsMyPC.Core.Services;

namespace ThisIsMyPC.Interop.Com.Shell;

public sealed class ShellExtensionService : IShellExtensionService
{
    private const string BlockedListKeyPath = @"HKLM\SOFTWARE\Microsoft\Windows\CurrentVersion\Shell Extensions\Blocked";

    private static readonly (string Path, string AppliesTo)[] HandlerRegistrations =
    [
        (@"HKCR\*\shellex\ContextMenuHandlers", "All files"),
        (@"HKCR\AllFilesystemObjects\shellex\ContextMenuHandlers", "All filesystem objects"),
        (@"HKCR\Directory\shellex\ContextMenuHandlers", "Directories"),
        (@"HKCR\Directory\Background\shellex\ContextMenuHandlers", "Folder background"),
        (@"HKCR\Folder\shellex\ContextMenuHandlers", "Folders"),
        (@"HKCR\Drive\shellex\ContextMenuHandlers", "Drives"),
        (@"HKCR\DesktopBackground\shellex\ContextMenuHandlers", "Desktop background"),
        (@"HKCR\CLSID\{645FF040-5081-101B-9F08-00AA002F954E}\shellex\ContextMenuHandlers", "Recycle Bin"),
        (@"HKCR\CLSID\{20D04FE0-3AEA-1069-A2D8-08002B30309D}\shellex\ContextMenuHandlers", "This PC"),
        (@"HKCR\CLSID\{F02C1A0D-BE21-4350-88B0-7367FC96EF3C}\shellex\ContextMenuHandlers", "Network"),
        (@"HKCR\SystemFileAssociations\Directory.Audio\shellex\ContextMenuHandlers", "Audio folders"),
        (@"HKCR\SystemFileAssociations\Directory.Video\shellex\ContextMenuHandlers", "Video folders"),
    ];

    private readonly IRegistryService _registryService;

    public ShellExtensionService(IRegistryService registryService)
    {
        _registryService = registryService;
    }

    public OperationResult<IReadOnlyList<ShellExtensionInfo>> EnumerateContextMenuHandlers()
    {
        try
        {
            var handlers = new List<ShellExtensionInfo>();
            var dllPathCache = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);
            var publisherCache = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);

            foreach (var (basePath, appliesTo) in HandlerRegistrations)
            {
                var subKeysResult = _registryService.EnumerateSubKeys(basePath);
                if (!subKeysResult.IsSuccess)
                    continue;

                foreach (var handlerName in subKeysResult.Value!)
                {
                    var handlerKeyPath = $@"{basePath}\{handlerName}";
                    var clsidResult = _registryService.ReadString(handlerKeyPath, string.Empty);
                    if (!clsidResult.IsSuccess || string.IsNullOrWhiteSpace(clsidResult.Value))
                        continue;

                    var rawClsid = clsidResult.Value!;
                    var isEnabled = !rawClsid.StartsWith('-');
                    var cleanClsid = isEnabled ? rawClsid : rawClsid[1..];

                    // Some handlers use an inverted registration: key name is the CLSID,
                    // default value is the friendly name (e.g., Taskband Pin, Start Menu Pin).
                    // Detect this and swap so Clsid always holds the actual CLSID.
                    var resolvedName = handlerName;
                    if (!LooksLikeClsid(cleanClsid) && LooksLikeClsid(handlerName))
                    {
                        resolvedName = cleanClsid;
                        cleanClsid = handlerName;
                    }

                    // Attempt to resolve a friendly display name from the CLSID registration
                    var registryKeyName = resolvedName;
                    var clsidDisplayName = ResolveClsidDisplayName(cleanClsid);
                    if (clsidDisplayName is not null)
                        resolvedName = clsidDisplayName;

                    // Cache DLL path lookups to avoid repeated InprocServer32 reads for the same CLSID
                    if (!dllPathCache.TryGetValue(cleanClsid, out var dllPath))
                    {
                        dllPath = ResolveDllPath(cleanClsid);
                        dllPathCache[cleanClsid] = dllPath;
                    }

                    // Cache publisher per DLL path to avoid redundant FileVersionInfo reads
                    string? publisher = null;
                    if (dllPath is not null && !publisherCache.TryGetValue(dllPath, out publisher))
                    {
                        publisher = ResolvePublisher(dllPath);
                        publisherCache[dllPath] = publisher;
                    }

                    handlers.Add(new ShellExtensionInfo(
                        HandlerName: resolvedName,
                        Clsid: cleanClsid,
                        RegistryPath: handlerKeyPath,
                        AppliesTo: appliesTo,
                        DllPath: dllPath,
                        Publisher: publisher,
                        IsEnabled: isEnabled,
                        RegistryKeyName: registryKeyName));
                }
            }

            // Note: blocked list state is NOT merged into IsEnabled here.
            // IsEnabled reflects only dash-prefix state from the registry value.
            // The scanner independently checks GetBlockedClsids() to determine
            // DisableMethod, avoiding conflation of two independent disable signals.

            return OperationResult<IReadOnlyList<ShellExtensionInfo>>.Success(handlers);
        }
        catch (Exception ex)
        {
            return OperationResult<IReadOnlyList<ShellExtensionInfo>>.Failure(
                $"Failed to enumerate context menu handlers: {ex.Message}",
                ErrorCategory.ServiceUnavailable, ex);
        }
    }

    public bool IsBlockedByCLSID(string clsid)
    {
        var result = _registryService.ValueExists(BlockedListKeyPath, clsid);
        return result.IsSuccess && result.Value;
    }

    public IReadOnlySet<string> GetBlockedClsids()
    {
        var result = _registryService.EnumerateValues(BlockedListKeyPath);
        if (!result.IsSuccess)
            return new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        return new HashSet<string>(result.Value!, StringComparer.OrdinalIgnoreCase);
    }

    private static readonly (string Path, string AppliesTo)[] DragDropRegistrations =
    [
        (@"HKCR\*\shellex\DragDropHandlers", "All files"),
        (@"HKCR\Directory\shellex\DragDropHandlers", "Directories"),
        (@"HKCR\Folder\shellex\DragDropHandlers", "Folders"),
    ];

    public OperationResult<IReadOnlyList<DragDropHandlerInfo>> EnumerateDragDropHandlers()
    {
        try
        {
            var handlers = new List<DragDropHandlerInfo>();
            var dllPathCache = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);
            var publisherCache = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);

            foreach (var (basePath, appliesTo) in DragDropRegistrations)
            {
                var subKeysResult = _registryService.EnumerateSubKeys(basePath);
                if (!subKeysResult.IsSuccess)
                    continue;

                foreach (var handlerName in subKeysResult.Value!)
                {
                    var handlerKeyPath = $@"{basePath}\{handlerName}";
                    var clsidResult = _registryService.ReadString(handlerKeyPath, string.Empty);
                    if (!clsidResult.IsSuccess || string.IsNullOrWhiteSpace(clsidResult.Value))
                        continue;

                    var cleanClsid = clsidResult.Value!;

                    // Same inverted CLSID detection as ContextMenuHandlers
                    var resolvedName = handlerName;
                    if (!LooksLikeClsid(cleanClsid) && LooksLikeClsid(handlerName))
                    {
                        resolvedName = cleanClsid;
                        cleanClsid = handlerName;
                    }

                    // Resolve CLSID display name (preserve original key name for registry view)
                    var registryKeyName = resolvedName;
                    var clsidDisplayName = ResolveClsidDisplayName(cleanClsid);
                    if (clsidDisplayName is not null)
                        resolvedName = clsidDisplayName;

                    if (!dllPathCache.TryGetValue(cleanClsid, out var dllPath))
                    {
                        dllPath = ResolveDllPath(cleanClsid);
                        dllPathCache[cleanClsid] = dllPath;
                    }

                    string? publisher = null;
                    if (dllPath is not null && !publisherCache.TryGetValue(dllPath, out publisher))
                    {
                        publisher = ResolvePublisher(dllPath);
                        publisherCache[dllPath] = publisher;
                    }

                    handlers.Add(new DragDropHandlerInfo(
                        Name: resolvedName,
                        Clsid: cleanClsid,
                        RegistryPath: handlerKeyPath,
                        AppliesTo: appliesTo,
                        DllPath: dllPath,
                        Publisher: publisher,
                        RegistryKeyName: registryKeyName));
                }
            }

            return OperationResult<IReadOnlyList<DragDropHandlerInfo>>.Success(handlers);
        }
        catch (Exception ex)
        {
            return OperationResult<IReadOnlyList<DragDropHandlerInfo>>.Failure(
                $"Failed to enumerate drag-drop handlers: {ex.Message}",
                ErrorCategory.ServiceUnavailable, ex);
        }
    }

    private string? ResolveClsidDisplayName(string clsid)
    {
        var clsidKeyPath = $@"HKCR\CLSID\{clsid}";
        var result = _registryService.ReadString(clsidKeyPath, string.Empty);
        if (!result.IsSuccess)
            return null;

        var value = result.Value!;

        // Skip empty, indirect strings (@dll,-ID), and values that look like CLSIDs
        if (string.IsNullOrWhiteSpace(value) || value.StartsWith('@') || LooksLikeClsid(value))
            return null;

        return value;
    }

    private string? ResolveDllPath(string clsid)
    {
        var inprocKeyPath = $@"HKCR\CLSID\{clsid}\InprocServer32";
        var result = _registryService.ReadString(inprocKeyPath, string.Empty);
        return result.IsSuccess ? result.Value : null;
    }

    private static bool LooksLikeClsid(string value) =>
        value.Length > 2 && value[0] == '{' && value[^1] == '}';

    private static string? ResolvePublisher(string? dllPath)
    {
        if (string.IsNullOrEmpty(dllPath) || !File.Exists(dllPath))
            return null;

        try
        {
            var versionInfo = FileVersionInfo.GetVersionInfo(dllPath);
            return versionInfo.CompanyName;
        }
        catch (Exception)
        {
            return null;
        }
    }
}
