using System.Diagnostics;
using ThisIsMyPC.Core.Results;
using ThisIsMyPC.Core.Services;

namespace ThisIsMyPC.Interop.Com.Shell;

public sealed class ShellExtensionService : IShellExtensionService
{
    private static readonly (string Path, string AppliesTo)[] HandlerRegistrations =
    [
        (@"HKCR\*\shellex\ContextMenuHandlers", "All files"),
        (@"HKCR\AllFilesystemObjects\shellex\ContextMenuHandlers", "All filesystem objects"),
        (@"HKCR\Directory\shellex\ContextMenuHandlers", "Directories"),
        (@"HKCR\Directory\Background\shellex\ContextMenuHandlers", "Folder background"),
        (@"HKCR\Folder\shellex\ContextMenuHandlers", "Folders"),
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

                    // Cache DLL path lookups to avoid repeated InprocServer32 reads for the same CLSID
                    if (!dllPathCache.TryGetValue(cleanClsid, out var dllPath))
                    {
                        dllPath = ResolveDllPath(cleanClsid);
                        dllPathCache[cleanClsid] = dllPath;
                    }

                    var publisher = ResolvePublisher(dllPath);

                    handlers.Add(new ShellExtensionInfo(
                        HandlerName: handlerName,
                        Clsid: cleanClsid,
                        RegistryPath: handlerKeyPath,
                        AppliesTo: appliesTo,
                        DllPath: dllPath,
                        Publisher: publisher,
                        IsEnabled: isEnabled));
                }
            }

            return OperationResult<IReadOnlyList<ShellExtensionInfo>>.Success(handlers);
        }
        catch (Exception ex)
        {
            return OperationResult<IReadOnlyList<ShellExtensionInfo>>.Failure(
                $"Failed to enumerate context menu handlers: {ex.Message}",
                ErrorCategory.ServiceUnavailable, ex);
        }
    }

    private string? ResolveDllPath(string clsid)
    {
        var inprocKeyPath = $@"HKCR\CLSID\{clsid}\InprocServer32";
        var result = _registryService.ReadString(inprocKeyPath, string.Empty);
        return result.IsSuccess ? result.Value : null;
    }

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
