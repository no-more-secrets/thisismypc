using System.Globalization;
using ThisIsMyPC.Core.Changes;
using ThisIsMyPC.Core.Services;
using ThisIsMyPC.Modules.WindowsUpdate.Models;

namespace ThisIsMyPC.Modules.WindowsUpdate.Services;

public sealed class WindowsUpdateSettingsReader
{
    private readonly IRegistryService _registryService;

    public WindowsUpdateSettingsReader(IRegistryService registryService)
    {
        _registryService = registryService;
    }

    /// <summary>The four single-value policy toggles, in module display order.</summary>
    public IReadOnlyList<UpdatePolicySetting> ReadSingles()
    {
        return
        [
            ReadSetting(
                id: "auto-update-mode",
                displayName: "Notify before downloading updates",
                description: "Windows Update announces available updates instead of installing them on its own schedule (AUOptions = 2). You choose when to download and install.",
                keyPath: WindowsUpdateRegistryPaths.AuPoliciesKeyPath,
                valueName: "AUOptions",
                configuredValue: "2"),

            ReadSetting(
                id: "no-auto-reboot",
                displayName: "Never auto-restart while you are signed in",
                description: "Stops Windows Update from rebooting to finish installing updates while anyone is signed in; the policy behind lost overnight work.",
                keyPath: WindowsUpdateRegistryPaths.AuPoliciesKeyPath,
                valueName: "NoAutoRebootWithLoggedOnUsers",
                configuredValue: "1"),

            ReadSetting(
                id: "exclude-drivers",
                displayName: "Keep drivers out of Windows Update",
                description: "Stops quality updates from replacing OEM GPU, audio, and chipset drivers with generic or outdated WHQL versions. Install driver updates from the vendor instead.",
                keyPath: WindowsUpdateRegistryPaths.WindowsUpdatePoliciesKeyPath,
                valueName: "ExcludeWUDriversInQualityUpdate",
                configuredValue: "1"),

            ReadSetting(
                id: "delivery-optimization",
                displayName: "Disable update peer-to-peer sharing",
                description: "Downloads updates from Microsoft's servers only (DODownloadMode = 0). Stops Delivery Optimization from uploading update chunks to other PCs on your connection.",
                keyPath: WindowsUpdateRegistryPaths.DeliveryOptimizationPoliciesKeyPath,
                valueName: "DODownloadMode",
                configuredValue: "0"),
        ];
    }

    /// <summary>
    /// The version-pin group: TargetReleaseVersion FIRST (its "1" is the group's toggle
    /// value per the set convention), then ProductVersion and TargetReleaseVersionInfo.
    /// Empty when DisplayVersion is unreadable; pinning to an unknown release would
    /// pin to nothing.
    /// </summary>
    public IReadOnlyList<UpdatePolicySetting> ReadVersionPin()
    {
        var displayVersion = ReadDisplayVersion();
        if (displayVersion.Length == 0)
            return [];

        var description = $"Pins the machine to its current Windows release ({displayVersion}) so feature upgrades wait until you remove the pin. Security and quality updates keep installing normally.";

        return
        [
            ReadSetting(
                id: "version-pin",
                displayName: "Stay on the current Windows version",
                description: description,
                keyPath: WindowsUpdateRegistryPaths.WindowsUpdatePoliciesKeyPath,
                valueName: "TargetReleaseVersion",
                configuredValue: "1"),

            ReadSetting(
                id: "version-pin",
                displayName: "Stay on the current Windows version",
                description: description,
                keyPath: WindowsUpdateRegistryPaths.WindowsUpdatePoliciesKeyPath,
                valueName: "ProductVersion",
                configuredValue: "Windows 11",
                valueType: ChangeValueType.Registry_String),

            ReadSetting(
                id: "version-pin",
                displayName: "Stay on the current Windows version",
                description: description,
                keyPath: WindowsUpdateRegistryPaths.WindowsUpdatePoliciesKeyPath,
                valueName: "TargetReleaseVersionInfo",
                configuredValue: displayVersion,
                valueType: ChangeValueType.Registry_String),
        ];
    }

    /// <summary>
    /// The Settings-page state toggles (UX\Settings, not the policy hive): what the
    /// Windows Update settings page itself writes. No GPCache, no SKU restriction.
    /// </summary>
    public IReadOnlyList<UpdatePolicySetting> ReadUxSettings()
    {
        return
        [
            ReadSetting(
                id: "restart-notifications",
                displayName: "Notify when a restart is required",
                description: "Shows a notification when Windows needs to restart to finish updating.",
                keyPath: WindowsUpdateRegistryPaths.UxSettingsKeyPath,
                valueName: "RestartNotificationsAllowed2",
                configuredValue: "1"),

            ReadSetting(
                id: "active-hours-manual",
                displayName: "Set active hours manually",
                description: "Stops Windows from adjusting active hours based on usage. Set the hours in Windows Settings.",
                keyPath: WindowsUpdateRegistryPaths.UxSettingsKeyPath,
                valueName: "SmartActiveHoursState",
                configuredValue: "2"),

            ReadSetting(
                id: "continuous-innovation",
                displayName: "Wait for broad rollout of new features",
                description: "Opts out of receiving the latest feature updates as soon as they are available.",
                keyPath: WindowsUpdateRegistryPaths.UxSettingsKeyPath,
                valueName: "IsContinuousInnovationOptedIn",
                configuredValue: "0"),
        ];
    }

    public WindowsUpdateScanData ReadAll() => new(ReadSingles(), ReadVersionPin(), ReadUxSettings());

    /// <summary>
    /// The live feature release, e.g. "24H2". Never derived from ProductName; it
    /// reports "Windows 10" on Windows 11 machines.
    /// </summary>
    public string ReadDisplayVersion()
    {
        var result = _registryService.ReadString(
            WindowsUpdateRegistryPaths.CurrentVersionKeyPath, "DisplayVersion");
        return result.IsSuccess ? result.Value!.Trim() : string.Empty;
    }

    private UpdatePolicySetting ReadSetting(
        string id,
        string displayName,
        string description,
        string keyPath,
        string valueName,
        string configuredValue,
        ChangeValueType valueType = ChangeValueType.Registry_DWord)
    {
        // Absent (or wrong-typed) value reads as "" = Not configured.
        string currentValue;
        if (valueType == ChangeValueType.Registry_String)
        {
            var read = _registryService.ReadString(keyPath, valueName);
            currentValue = read.IsSuccess ? read.Value! : string.Empty;
        }
        else
        {
            var read = _registryService.ReadDWord(keyPath, valueName);
            currentValue = read.IsSuccess
                ? read.Value.ToString(CultureInfo.InvariantCulture)
                : string.Empty;
        }

        return new UpdatePolicySetting(
            Id: id,
            DisplayName: displayName,
            Description: description,
            RegistryKeyPath: keyPath,
            RegistryValueName: valueName,
            ValueType: valueType,
            CurrentValue: currentValue,
            ConfiguredValue: configuredValue);
    }
}
