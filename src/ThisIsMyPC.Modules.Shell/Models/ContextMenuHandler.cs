using ThisIsMyPC.Interop.Com.Shell;

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
    IReadOnlyDictionary<string, bool>? PathEnabledStates = null,
    IReadOnlySet<ContextMenuSurface>? VisibleSurfaces = null,
    DisableMethod DisableMethod = DisableMethod.None,
    HandlerType HandlerType = HandlerType.ComHandler,
    StaticVerbInfo? VerbInfo = null,
    ModernPackagedInfo? PackagedInfo = null,
    bool IsDualRegistered = false,
    string? DualRegistrationPartnerName = null)
{
    public bool IsBlockedListDisabled => DisableMethod is DisableMethod.BlockedList or DisableMethod.Both;
}
