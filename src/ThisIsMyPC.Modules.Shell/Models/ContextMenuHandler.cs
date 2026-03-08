namespace ThisIsMyPC.Modules.Shell.Models;

public sealed record ContextMenuHandler(
    string Name,
    string Clsid,
    string RegistryPath,
    string AppliesTo,
    string? DllPath,
    string? Publisher,
    bool IsEnabled,
    HandlerClassification Classification = HandlerClassification.ThirdParty,
    IReadOnlyList<string>? AllRegistryPaths = null,
    IReadOnlyList<string>? AllScopes = null,
    IReadOnlyDictionary<string, bool>? PathEnabledStates = null);
