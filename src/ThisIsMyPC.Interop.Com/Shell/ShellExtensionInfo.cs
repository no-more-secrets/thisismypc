namespace ThisIsMyPC.Interop.Com.Shell;

public sealed record ShellExtensionInfo(
    string HandlerName,
    string Clsid,
    string RegistryPath,
    string AppliesTo,
    string? DllPath,
    string? Publisher,
    bool IsEnabled,
    string? RegistryKeyName = null)
{
    /// <summary>
    /// The raw registry key name under ContextMenuHandlers. Falls back to HandlerName if not set.
    /// </summary>
    public string EffectiveRegistryKeyName => RegistryKeyName ?? HandlerName;
}
