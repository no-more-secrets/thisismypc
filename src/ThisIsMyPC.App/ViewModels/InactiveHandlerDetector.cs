using ThisIsMyPC.Core.Services;
using ThisIsMyPC.Modules.Shell.Models;

namespace ThisIsMyPC.App.ViewModels;

/// <summary>
/// Detects whether a handler is currently inactive on this PC by checking system state.
/// Inactive handlers are real Windows features that require specific configuration to function.
/// </summary>
internal sealed class InactiveHandlerDetector
{
    private readonly bool _workFoldersUnconfigured;
    private readonly bool _offlineFilesDisabled;
    private readonly bool _slideshowInactive;
    private readonly bool _stickersInactive;

    public InactiveHandlerDetector(IRegistryService registry)
    {
        _workFoldersUnconfigured = IsWorkFoldersUnconfigured(registry);
        _offlineFilesDisabled = IsOfflineFilesDisabled(registry);
        _slideshowInactive = IsSlideshowInactive(registry);
        _stickersInactive = IsStickersInactive(registry);
    }

    /// <summary>
    /// Returns (isInactive, reason) for the given handler. Returns (false, null) if the handler
    /// is active or if we can't determine its state.
    /// </summary>
    public (bool IsInactive, string? Reason) Check(ContextMenuHandlerViewModel vm)
    {
        // COM handlers; check by CLSID (case-insensitive)
        var clsid = vm.Clsid.ToUpperInvariant();
        return clsid switch
        {
            "{A470F8CF-A1E8-4F65-8335-227475AA5C46}" =>
                (true, "EFS encryption is available but this handler no longer adds a context menu entry on Windows 11. Use file Properties > Advanced to encrypt."),

            "{E61BF828-5E63-4287-BEF1-60B1A4FDE0E3}" when _workFoldersUnconfigured =>
                (true, "Work Folders is not configured. Set up Work Folders in Settings > Accounts to enable sync options."),

            "{474C98EE-CF3D-41F5-80E3-4AAB0AB04301}" when _offlineFilesDisabled =>
                (true, "Offline Files is disabled. Enable it in Control Panel > Sync Center to use offline file caching."),

            "{CB3D0F55-BC2C-4C1A-85ED-23ED75B5106B}" =>
                (true, "OneDrive uses modern shell integration for menu entries. This legacy COM handler has no visible effect."),

            "{2A118EB5-5797-4F5E-8B3D-F4ECBA3C98E4}" =>
                (true, "Adobe Creative Cloud sync overlay. No visible context menu entry; only provides folder sync status."),

            "{90AA3A4E-1CBA-4233-B8BB-535773D48449}" =>
                (true, "Only appears when right-clicking executable (.exe) files, not general files or folders."),

            "{2854F705-3548-414C-A113-93E27C808C85}" =>
                (true, "Only appears on drives that support Enhanced Storage (encrypted USB with IEEE 1667)."),

            "{0BF754AA-C967-445C-AB3D-D8FDA9BAE7EF}" when _slideshowInactive =>
                (true, "Only appears on the desktop when wallpaper is set to slideshow mode."),

            _ => CheckStaticVerb(vm),
        };
    }

    private (bool IsInactive, string? Reason) CheckStaticVerb(ContextMenuHandlerViewModel vm)
    {
        if (vm.HandlerType != HandlerType.StaticVerb)
            return (false, null);

        var verbName = vm.VerbInfo?.VerbName;

        // EFS encrypt verbs; handler doesn't surface menu entry on Win11
        if (vm.Label.Contains("efscore", StringComparison.OrdinalIgnoreCase))
            return (true, "EFS verb; no visible context menu entry on Windows 11.");

        // Work Folders verb (identified by DLL reference in name)
        if (vm.Label.Contains("WorkfoldersControl", StringComparison.OrdinalIgnoreCase) && _workFoldersUnconfigured)
            return (true, "Work Folders is not configured on this PC.");

        // CSC / Offline Files verb
        if (vm.Label.Contains("cscui", StringComparison.OrdinalIgnoreCase) && _offlineFilesDisabled)
            return (true, "Offline Files is disabled on this PC.");

        // EditStickers; desktop feature that may be disabled
        if (verbName is not null && verbName.Equals("EditStickers", StringComparison.OrdinalIgnoreCase) && _stickersInactive)
            return (true, "Desktop stickers feature. Only visible when enabled in Personalization settings.");

        return (false, null);
    }

    private static bool IsWorkFoldersUnconfigured(IRegistryService registry)
    {
        var svcResult = registry.ReadDWord(
            @"HKLM\SYSTEM\CurrentControlSet\Services\workfolderssvc", "Start");
        // Service doesn't exist or is disabled
        return !svcResult.IsSuccess || svcResult.Value == 4;
    }

    private static bool IsOfflineFilesDisabled(IRegistryService registry)
    {
        var svcResult = registry.ReadDWord(
            @"HKLM\SYSTEM\CurrentControlSet\Services\CscService", "Start");
        // Service doesn't exist or is disabled
        return !svcResult.IsSuccess || svcResult.Value == 4;
    }

    private static bool IsSlideshowInactive(IRegistryService registry)
    {
        var result = registry.ReadDWord(
            @"HKCU\SOFTWARE\Microsoft\Windows\CurrentVersion\Explorer\Wallpapers", "SlideshowEnabled");
        // Not set or 0 = no slideshow
        return !result.IsSuccess || result.Value == 0;
    }

    private static bool IsStickersInactive(IRegistryService registry)
    {
        var result = registry.KeyExists(
            @"HKCU\SOFTWARE\Microsoft\Windows\CurrentVersion\Explorer\Stickers");
        // Key doesn't exist = feature not enabled
        return !result.IsSuccess || !result.Value;
    }
}
