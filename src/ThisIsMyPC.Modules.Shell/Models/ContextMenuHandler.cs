namespace ThisIsMyPC.Modules.Shell.Models;

public sealed record ContextMenuHandler(
    string Name,
    string Clsid,
    string RegistryPath,
    string AppliesTo,
    string? DllPath,
    string? Publisher,
    bool IsEnabled);
