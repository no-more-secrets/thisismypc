namespace ThisIsMyPC.Interop.Com.Shell;

public sealed record DragDropHandlerInfo(
    string Name,
    string Clsid,
    string RegistryPath,
    string AppliesTo,
    string? DllPath,
    string? Publisher,
    string? RegistryKeyName = null);
