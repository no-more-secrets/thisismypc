namespace ThisIsMyPC.Interop.Com.Shell;

public sealed record ShellExtensionInfo(
    string HandlerName,
    string Clsid,
    string RegistryPath,
    string AppliesTo,
    string? DllPath,
    string? Publisher,
    bool IsEnabled);
