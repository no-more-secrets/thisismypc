namespace ThisIsMyPC.Core.Policies;

/// <summary>Policy overrides of existing controls. Scopes follow the installed Windows ADMX definitions.</summary>
public static class PolicyControlCatalog
{
    private const string Machine = @"HKLM\Software\Policies\Microsoft\Windows\";
    private const string User = @"HKCU\Software\Policies\Microsoft\Windows\";
    private const string Cloud = "CloudContent\\";
    private const string Explorer = @"Software\Microsoft\Windows\CurrentVersion\Policies\Explorer\";
    public static IReadOnlyList<PolicyControlRule> Rules { get; } = Build();

    public static IReadOnlyList<string> CompanionLocations(string module, string setting) => (module, setting) switch
    {
        ("Windows Annoyances", "copilot") => [Machine + @"WindowsCopilot\TurnOffWindowsCopilot", User + @"WindowsCopilot\TurnOffWindowsCopilot"],
        ("Windows Annoyances", "recall") => [Machine + @"WindowsAI\AllowRecallEnablement", Machine + @"WindowsAI\DisableAIDataAnalysis", User + @"WindowsAI\DisableAIDataAnalysis", Machine + @"WindowsAI\TurnOffSavingSnapshots"],
        ("Windows Annoyances", "bing-search") => [User + @"Explorer\DisableSearchBoxSuggestions"],
        ("Windows Annoyances", "activity-history") => [Machine + @"System\EnableActivityFeed", Machine + @"System\PublishUserActivities", Machine + @"System\UploadUserActivities"],
        ("Windows Annoyances", "edge-debloat") => [@"HKLM\SOFTWARE\Policies\Microsoft\Edge\EdgeShoppingAssistantEnabled", @"HKLM\SOFTWARE\Policies\Microsoft\Edge\ShowMicrosoftRewards", @"HKLM\SOFTWARE\Policies\Microsoft\Edge\PersonalizationReportingEnabled"],
        ("Windows Update", "version-pin") => [Machine + @"WindowsUpdate\TargetReleaseVersion", Machine + @"WindowsUpdate\ProductVersion", Machine + @"WindowsUpdate\TargetReleaseVersionInfo"],
        ("Power", "allow-sleep") => [@"HKLM\SOFTWARE\Policies\Microsoft\Power\PowerSettings\abfc2519-3608-4c2a-94ea-171b0ed546ab\ACSettingIndex", @"HKLM\SOFTWARE\Policies\Microsoft\Power\PowerSettings\abfc2519-3608-4c2a-94ea-171b0ed546ab\DCSettingIndex"],
        _ => [],
    };

    private static IReadOnlyList<PolicyControlRule> Build()
    {
        var rules = new List<PolicyControlRule>();
        void Add(string module, string id, string path, int value, string message, bool? toggle = true, bool blocks = true) =>
            rules.Add(new(module, id, [new(path, value)], message, toggle, blocks));
        void Annoyance(string id, string path, int value, string message, bool blocks = true) =>
            Add("Windows Annoyances", id, path, value, message, blocks ? true : null, blocks);

        Add("Windows Update", "auto-update-mode", Machine + @"WindowsUpdate\AU\NoAutoUpdate", 1,
            "Automatic updates are disabled by policy. Updates must be started manually.", null);
        Add("Privacy & Telemetry", "telemetry-level", Machine + @"DataCollection\AllowTelemetry", 0,
            "Diagnostic data policy is set to Off. The stricter policy is kept.", null);
        Add("Privacy & Telemetry", "telemetry-level", User + @"DataCollection\AllowTelemetry", 0,
            "The user diagnostic data policy is set to Off. Review both policy scopes before changing the machine policy.", null);
        Add("Privacy & Telemetry", "online-speech", @"HKLM\Software\Policies\Microsoft\InputPersonalization\AllowInputPersonalization", 0,
            "Online speech recognition is disabled by policy.");
        Add("Privacy & Telemetry", "app-launch-tracking", "HKCU\\" + Explorer + "NoInstrumentation", 1,
            "App launch tracking is disabled by policy.");
        Add("Privacy & Telemetry", "location", User + @"LocationAndSensors\DisableLocation", 1,
            "Location is disabled by user policy. The machine policy is separate.", null, false);
        Add("Privacy & Telemetry", "error-reporting", User + @"Windows Error Reporting\Disabled", 1,
            "Error reporting is disabled by user policy. The machine policy and service are separate.", null, false);

        Annoyance("welcome-experience", User + Cloud + "DisableWindowsSpotlightWindowsWelcomeExperience", 1, "The Windows welcome experience is disabled by policy.");
        Annoyance("app-suggestions", User + Cloud + "DisableThirdPartySuggestions", 1, "Third-party suggestions are disabled by policy.");
        Annoyance("windows-tips", Machine + Cloud + "DisableSoftLanding", 1, "Windows tips are disabled by policy.");
        Annoyance("silent-app-installs", Machine + Cloud + "DisableWindowsConsumerFeatures", 1, "Consumer app installation is disabled by policy.");
        Annoyance("settings-suggested-content", User + Cloud + "DisableWindowsSpotlightOnSettings", 1, "Suggested content in Settings is disabled by policy.");
        Annoyance("tailored-experiences", User + Cloud + "DisableTailoredExperiencesWithDiagnosticData", 1, "Tailored experiences are disabled by policy.");
        Annoyance("advertising-id", Machine + @"AdvertisingInfo\DisabledByGroupPolicy", 1, "The advertising ID is disabled by policy.");
        Annoyance("feedback-frequency", Machine + @"DataCollection\DoNotShowFeedbackNotifications", 1, "Feedback notifications are disabled by policy.");
        Annoyance("dynamic-search-box", Machine + @"Windows Search\EnableDynamicContentInWSB", 0, "Search highlights are disabled by policy.");
        Annoyance("game-dvr", Machine + @"GameDVR\AllowGameDVR", 0, "Game recording is disabled by policy.");
        foreach (var id in new[] { "welcome-experience", "app-suggestions", "settings-suggested-content", "lock-screen-images", "lock-screen-ads", "spotlight-collection-desktop" })
            Annoyance(id, User + Cloud + "DisableWindowsSpotlightFeatures", 1,
                "Windows Spotlight features are disabled by policy. Related preferences remain separate.", blocks: false);
        // These policies cover only part of a grouped control. Do not claim the whole group is applied.
        Annoyance("preinstalled-apps", Machine + Cloud + "DisableWindowsConsumerFeatures", 1, "Consumer app promotions are disabled by policy. Other promotion preferences are separate.", blocks: false);
        Annoyance("preinstalled-apps", Machine + Cloud + "DisableSoftLanding", 1, "Windows tips are disabled by policy. Other promotion preferences are separate.", blocks: false);

        foreach (var hive in new[] { "HKLM\\", "HKCU\\" })
        {
            Add("Explorer", "quick-access-recent-files", hive + Explorer + "NoRecentDocsHistory", 1, "Recent document history is disabled by policy.", false);
            Add("Explorer", "quick-access-frequent-folders", hive + Explorer + "NoRecentDocsHistory", 1, "Recent document history is disabled by policy. Frequent-folder history may also be affected.", null, false);
        }
        Add("Explorer", "taskbar-widgets", @"HKLM\SOFTWARE\Policies\Microsoft\Dsh\AllowNewsAndInterests", 0, "Widgets are disabled by policy.", false);
        Add("Explorer", "meet-now", "HKCU\\" + Explorer + "HideSCAMeetNow", 1, "Meet Now is hidden by policy.", false);
        var searchNames = new[] { "Hidden", "Search icon", "Search icon and label", "Search box" };
        for (var value = 0; value < searchNames.Length; value++)
            rules.Add(new("Explorer", "taskbar-search-mode", [new(Machine + @"Windows Search\SearchOnTaskbarMode", value, [0, 1, 2, 3])],
                "Taskbar search is controlled by policy: " + searchNames[value] + ".", null, true, value));
        rules.Add(new("Explorer", "taskbar-button-combining", [new("HKCU\\" + Explorer + "NoTaskGrouping", 1)],
            "A legacy taskbar grouping policy is configured. Its effect on this taskbar is not verified.", null, false));
        Add("Windows Update", "active-hours-manual", Machine + @"WindowsUpdate\SetActiveHours", 1, "Active hours are configured by policy.", true);
        rules.Add(new("Windows Update", "restart-notifications",
            [new(Machine + @"WindowsUpdate\SetUpdateNotificationLevel", 1), new(Machine + @"WindowsUpdate\UpdateNotificationLevel", 2)],
            "Update and restart notifications are disabled by policy.", false, true));
        return rules;
    }
}

public sealed record PolicyCondition(string Location, int Value, IReadOnlyList<int>? ValidValues = null);
public sealed record PolicyControlRule(string ModuleId, string SettingId, IReadOnlyList<PolicyCondition> Conditions,
    string Message, bool? ToggleState, bool BlocksChanges, int? ChoiceValue = null);
