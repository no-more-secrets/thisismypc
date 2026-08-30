using ThisIsMyPC.Core.Changes;
using ThisIsMyPC.Modules.Startup.Models;
using ThisIsMyPC.Modules.Startup.Services;

namespace ThisIsMyPC.Modules.Startup.Changes;

/// <summary>
/// Builds ChangeDescriptors that toggle startup entries via Windows'
/// StartupApproved REG_BINARY state; the same non-destructive mechanism
/// Task Manager uses. The Run value / .lnk file itself is never touched.
/// </summary>
public static class StartupChangeFactory
{
    private const string ModuleId = "Startup & Services";

    /// <summary>12-byte StartupApproved blob meaning enabled (even first byte).</summary>
    public static readonly byte[] EnabledBlob = [0x02, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0];

    /// <summary>12-byte StartupApproved blob meaning disabled (odd first byte; bytes 4-11 are an optional disable-time FILETIME).</summary>
    public static readonly byte[] DisabledBlob = [0x03, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0];

    /// <summary>StartupApproved key for a source; null for sources this factory cannot toggle (scheduled tasks; Story 3.4).</summary>
    public static string? GetApprovedKeyPath(StartupSource source) => source switch
    {
        StartupSource.RegistryMachineRun => StartupScanner.MachineApprovedRunKey,
        StartupSource.RegistryMachineRunWow64 => StartupScanner.MachineApprovedRun32Key,
        StartupSource.RegistryUserRun => StartupScanner.UserApprovedRunKey,
        StartupSource.StartupFolderUser => StartupScanner.UserApprovedStartupFolderKey,
        StartupSource.StartupFolderCommon => StartupScanner.MachineApprovedStartupFolderKey,
        _ => null,
    };

    public static string GetSettingId(StartupEntry entry) => $"startup-entry:{entry.Source}:{entry.Name}";

    /// <summary>
    /// currentApprovedBlob is the live StartupApproved value (null when absent;
    /// absent means enabled and reverting recreates the absence by deleting the value).
    /// A present-but-empty blob also encodes as empty BeforeValue, so revert deletes
    /// it instead of rewriting zero bytes; functionally identical (both read enabled).
    /// </summary>
    public static ChangeDescriptor? CreateToggle(StartupEntry entry, bool enable, byte[]? currentApprovedBlob)
    {
        var approvedKey = GetApprovedKeyPath(entry.Source);
        if (approvedKey is null)
            return null;

        var beforeEnabled = currentApprovedBlob is null || currentApprovedBlob.Length == 0 || (currentApprovedBlob[0] & 1) == 0;

        return new ChangeDescriptor
        {
            ModuleId = ModuleId,
            SettingId = GetSettingId(entry),
            DisplayName = $"Startup entry: {entry.Name}",
            SystemLocation = $@"{approvedKey}\{entry.Name}",
            BeforeValue = currentApprovedBlob is null ? string.Empty : Convert.ToHexString(currentApprovedBlob),
            AfterValue = Convert.ToHexString(enable ? EnabledBlob : DisabledBlob),
            BeforeDisplay = beforeEnabled ? "Enabled" : "Disabled",
            AfterDisplay = enable ? "Enabled" : "Disabled",
            ValueType = ChangeValueType.Registry_Binary,
            Category = enable ? ChangeCategory.Enable : ChangeCategory.Disable,
            RestartRequirement = RestartRequirement.None,
        };
    }
}
