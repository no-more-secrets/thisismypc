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

        return result.Value!.Select(info => new ContextMenuHandler(
            Name: info.HandlerName,
            Clsid: info.Clsid,
            RegistryPath: info.RegistryPath,
            AppliesTo: info.AppliesTo,
            DllPath: info.DllPath,
            Publisher: info.Publisher,
            IsEnabled: info.IsEnabled)).ToList();
    }
}
