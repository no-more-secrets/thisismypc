using ThisIsMyPC.Core.Changes;
using ThisIsMyPC.Core.Modules;

namespace ThisIsMyPC.Core.Policies;

/// <summary>Exact local computer policies editable by existing Annoyances toggles.</summary>
public static class LocalPolicyToggleCatalog
{
    public const string CloudKey = @"HKLM\SOFTWARE\Policies\Microsoft\Windows\CloudContent";

    public static string? Location(string module, string setting) => module == "Windows Annoyances" ? setting switch
    {
        "windows-tips" => CloudKey + @"\DisableSoftLanding",
        "consumer-features" or "silent-app-installs" => CloudKey + @"\DisableWindowsConsumerFeatures",
        _ => null,
    } : null;

    public static bool Allows(ChangeDescriptor change) =>
        change.ValueType == ChangeValueType.LocalPolicy_DWord
        && Location(change.ModuleId, change.SettingId) is { } location
        && location.Equals(change.SystemLocation, StringComparison.OrdinalIgnoreCase)
        && change.Enforcement is { SkuRestriction: WindowsSku.Enterprise, AclElevation: false, OwnerModeRequired: false }
        && change.Enforcement.CompanionServices is not { Count: > 0 }
        && change.Enforcement.CompanionTasks is not { Count: > 0 }
        && change.Enforcement.GPCacheEntries is not { Count: > 0 }
        && change.Enforcement.ReversionVectors is not { Count: > 0 }
        && Valid(LocalPolicyValue.Decode(change.BeforeValue)) && Valid(LocalPolicyValue.Decode(change.AfterValue));

    public static bool Valid(LocalPolicyValue? value) => value is not null
        && value.Live is "" or "0" or "1" && value.Saved is null or "0" or "1";
}
