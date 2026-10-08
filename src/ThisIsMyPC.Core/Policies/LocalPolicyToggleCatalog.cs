using ThisIsMyPC.Core.Changes;
using ThisIsMyPC.Core.Modules;

namespace ThisIsMyPC.Core.Policies;

/// <summary>Exact local computer policies editable by existing Annoyances toggles.</summary>
public static class LocalPolicyToggleCatalog
{
    public const string CloudKey = @"HKLM\SOFTWARE\Policies\Microsoft\Windows\CloudContent";

    private const string Machine = @"HKLM\SOFTWARE\Policies\Microsoft\Windows\";
    private const string User = @"HKCU\SOFTWARE\Policies\Microsoft\Windows\";
    private const string Edge = @"HKLM\SOFTWARE\Policies\Microsoft\Edge\";

    public static IReadOnlyList<PolicyToggleTarget> Targets(string module, string setting) => module != "Windows Annoyances" ? [] : setting switch
    {
        "windows-tips" => [new(CloudKey + @"\DisableSoftLanding", "1")],
        "consumer-features" => [new(CloudKey + @"\DisableWindowsConsumerFeatures", "1", Direct: true)],
        "silent-app-installs" => [new(CloudKey + @"\DisableWindowsConsumerFeatures", "1")],
        "welcome-experience" => [new(User + @"CloudContent\DisableWindowsSpotlightWindowsWelcomeExperience", "1")],
        "app-suggestions" => [new(User + @"CloudContent\DisableThirdPartySuggestions", "1")],
        "settings-suggested-content" => [new(User + @"CloudContent\DisableWindowsSpotlightOnSettings", "1")],
        "tailored-experiences" => [new(User + @"CloudContent\DisableTailoredExperiencesWithDiagnosticData", "1")],
        "advertising-id" => [new(Machine + @"AdvertisingInfo\DisabledByGroupPolicy", "1")],
        "feedback-frequency" => [new(Machine + @"DataCollection\DoNotShowFeedbackNotifications", "1")],
        "dynamic-search-box" => [new(Machine + @"Windows Search\EnableDynamicContentInWSB", "0")],
        "game-dvr" => [new(Machine + @"GameDVR\AllowGameDVR", "0")],
        "spotlight-features" => [new(User + @"CloudContent\DisableWindowsSpotlightFeatures", "1", Direct: true)],
        "spotlight-collection-desktop" => [new(User + @"CloudContent\DisableSpotlightCollectionOnDesktop", "1", Direct: true)],
        "edge-sidebar" => [new(Edge + "HubsSidebarEnabled", "0", Direct: true)],
        "edge-shortcuts" => [new(@"HKLM\SOFTWARE\Policies\Microsoft\EdgeUpdate\CreateDesktopShortcutDefault", "0", Direct: true)],
        "bing-search" => [new(User + @"Explorer\DisableSearchBoxSuggestions", "1", Direct: true, CoversControl: false)],
        "copilot" => [new(Machine + @"WindowsCopilot\TurnOffWindowsCopilot", "1", Direct: true),
            new(User + @"WindowsCopilot\TurnOffWindowsCopilot", "1", Direct: true)],
        "activity-history" => [new(Machine + @"System\EnableActivityFeed", "0", Direct: true),
            new(Machine + @"System\PublishUserActivities", "0", Direct: true),
            new(Machine + @"System\UploadUserActivities", "0", Direct: true)],
        "edge-debloat" => [new(Edge + "EdgeShoppingAssistantEnabled", "0", Direct: true),
            new(Edge + "ShowMicrosoftRewards", "0", Direct: true),
            new(Edge + "PersonalizationReportingEnabled", "0", Direct: true)],
        "preinstalled-apps" => [new(CloudKey + @"\DisableWindowsConsumerFeatures", "1", CoversControl: false),
            new(CloudKey + @"\DisableSoftLanding", "1", CoversControl: false)],
        _ => [],
    };

    // Kept for consumers that only need to identify a supported setting.
    public static string? Location(string module, string setting) => Targets(module, setting).FirstOrDefault()?.Location;

    public static bool Allows(ChangeDescriptor change)
    {
        var target = Targets(change.ModuleId, change.SettingId).FirstOrDefault(t => t.Location.Equals(change.SystemLocation, StringComparison.OrdinalIgnoreCase));
        return target is not null && change.ValueType == ChangeValueType.LocalPolicy_DWord
            && change.Enforcement is { AclElevation: false, OwnerModeRequired: false }
            && change.Enforcement.SkuRestriction == target.Edition
            && change.Enforcement.CompanionServices is not { Count: > 0 }
            && change.Enforcement.CompanionTasks is not { Count: > 0 }
            && change.Enforcement.GPCacheEntries is not { Count: > 0 }
            && change.Enforcement.ReversionVectors is not { Count: > 0 }
            && Valid(LocalPolicyValue.Decode(change.BeforeValue)) && Valid(LocalPolicyValue.Decode(change.AfterValue));
    }

    public static bool Valid(LocalPolicyValue? value) => value is not null
        && value.Live is "" or "0" or "1" && value.Saved is null or "0" or "1";
}

public sealed record PolicyToggleTarget(string Location, string Suppressed, bool Direct = false, bool CoversControl = true)
{
    public string Allowed => Suppressed == "1" ? "0" : "1";
    public WindowsSku Edition => Services.SettingEditionSupport.RequiredEdition(Location, null) ?? WindowsSku.Pro;
}
