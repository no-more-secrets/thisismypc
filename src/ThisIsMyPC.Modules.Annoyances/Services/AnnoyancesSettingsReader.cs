using ThisIsMyPC.Core.Changes;
using ThisIsMyPC.Core.Services;
using ThisIsMyPC.Modules.Annoyances.Models;

namespace ThisIsMyPC.Modules.Annoyances.Services;

public sealed class AnnoyancesSettingsReader
{
    private readonly IRegistryService _registryService;

    public AnnoyancesSettingsReader(IRegistryService registryService)
    {
        _registryService = registryService;
    }

    public IReadOnlyList<AnnoyancePreference> ReadAll()
    {
        var cdm = AnnoyancesRegistryPaths.ContentDeliveryManagerKeyPath;

        return
        [
            ReadPreference(
                id: "scoobe-nags",
                displayName: "Suppress \"Finish setting up your device\" nags",
                description: "Stops the full-screen SCOOBE prompts Windows shows after updates to push Microsoft 365, OneDrive, and account sign-in. There is no permanent opt-out in Settings — only \"Remind me in 3 days\".",
                keyPath: AnnoyancesRegistryPaths.UserProfileEngagementKeyPath,
                valueName: "ScoobeSystemSettingEnabled"),

            ReadPreference(
                id: "welcome-experience",
                displayName: "Suppress the Windows welcome experience",
                description: "Stops the \"Welcome to Windows\" full-screen page that appears after updates and occasionally when signing in.",
                keyPath: cdm,
                valueName: "SubscribedContent-310093Enabled"),

            ReadPreference(
                id: "app-suggestions",
                displayName: "Suppress Start menu app suggestions",
                description: "Stops Windows from suggesting Store apps to install in the Start menu.",
                keyPath: cdm,
                valueName: "SubscribedContent-338388Enabled"),

            ReadPreference(
                id: "windows-tips",
                displayName: "Suppress Windows tips and \"Get started\" prompts",
                description: "Stops tip notifications and \"Get the most out of Windows\" suggestion prompts.",
                keyPath: cdm,
                valueName: "SubscribedContent-338389Enabled"),

            ReadPreference(
                id: "settings-suggestions",
                displayName: "Suppress suggestions in Settings and Start",
                description: "Stops the occasional suggestion entries Windows injects into the Settings app and Start pane.",
                keyPath: cdm,
                valueName: "SystemPaneSuggestionsEnabled"),

            ReadPreference(
                id: "lock-screen-ads",
                displayName: "Suppress lock screen tips and ads",
                description: "Stops \"fun facts, tips, and tricks\" overlays on the lock screen when Windows Spotlight is active. The Spotlight wallpaper itself keeps working.",
                keyPath: cdm,
                valueName: "RotatingLockScreenOverlayEnabled"),

            ReadPreference(
                id: "silent-app-installs",
                displayName: "Suppress automatic promoted app installs",
                description: "Stops Windows from silently installing suggested Store apps (games, streaming apps) onto the Start menu.",
                keyPath: cdm,
                valueName: "SilentInstalledAppsEnabled"),

            ReadEdgeShortcutPreference(),

            ReadPreference(
                id: "advertising-id",
                displayName: "Disable the Advertising ID",
                description: "Stops apps from using your per-user Advertising ID to build a cross-app profile of you for targeted ads. The comprehensive privacy suite with companion service management arrives in the Privacy & Telemetry module.",
                keyPath: AnnoyancesRegistryPaths.AdvertisingInfoKeyPath,
                valueName: "Enabled",
                section: AnnoyanceSection.AdvertisingAndTracking),

            ReadPreference(
                id: "activity-history",
                displayName: "Disable activity history collection",
                description: "Stops Windows from collecting your local application activity history (the Timeline/Activity Feed). Full telemetry controls arrive in the Privacy & Telemetry module.",
                keyPath: AnnoyancesRegistryPaths.SystemPoliciesKeyPath,
                valueName: "EnableActivityFeed",
                section: AnnoyanceSection.AdvertisingAndTracking),

            ReadPreference(
                id: "game-dvr",
                displayName: "Disable Game DVR background recording",
                description: "Stops the Xbox Game Bar's always-on background video encoding, a common cause of micro-stutter in games (requires Explorer restart).",
                keyPath: AnnoyancesRegistryPaths.GameDvrKeyPath,
                valueName: "AppCaptureEnabled",
                section: AnnoyanceSection.GamingAndAccessibility,
                restart: RestartRequirement.ExplorerRestart),

            ReadPreference(
                id: "auto-game-mode",
                displayName: "Disable Auto Game Mode",
                description: "Stops Windows from automatically throttling background apps (Discord, OBS, browsers) whenever a full-screen game is detected.",
                keyPath: AnnoyancesRegistryPaths.GameBarKeyPath,
                valueName: "AutoGameModeEnabled",
                section: AnnoyanceSection.GamingAndAccessibility),

            ReadPreference(
                id: "hags",
                displayName: "Disable Hardware-Accelerated GPU Scheduling (HAGS)",
                description: "HAGS moves GPU task scheduling from the CPU onto the GPU itself. It is required for DLSS 3 Frame Generation but can cause stutter or instability in older games. A reboot is required for the change to take effect.",
                keyPath: AnnoyancesRegistryPaths.GraphicsDriversKeyPath,
                valueName: "HwSchMode",
                section: AnnoyanceSection.GamingAndAccessibility,
                suppressedValue: "1",
                defaultValue: "2",
                restart: RestartRequirement.Reboot),

            ReadPreference(
                id: "sticky-keys-shortcut",
                displayName: "Suppress the StickyKeys shortcut",
                description: "Stops pressing Shift five times from popping the StickyKeys prompt (which minimizes full-screen games). StickyKeys itself stays available from Settings. Takes effect after signing out and back in.",
                keyPath: AnnoyancesRegistryPaths.StickyKeysKeyPath,
                valueName: "Flags",
                section: AnnoyanceSection.GamingAndAccessibility,
                valueType: ChangeValueType.Registry_String,
                suppressedValue: "506",
                defaultValue: "510",
                // Raw registry Flags edits are read at logon; live toggling goes through
                // SystemParametersInfo, which this module deliberately doesn't use.
                restart: RestartRequirement.SignOut),

            ReadPreference(
                id: "filter-keys-shortcut",
                displayName: "Suppress the FilterKeys shortcut",
                description: "Stops holding Shift for eight seconds from popping the FilterKeys prompt mid-typing. FilterKeys itself stays available from Settings. Takes effect after signing out and back in.",
                keyPath: AnnoyancesRegistryPaths.KeyboardResponseKeyPath,
                valueName: "Flags",
                section: AnnoyanceSection.GamingAndAccessibility,
                valueType: ChangeValueType.Registry_String,
                suppressedValue: "122",
                defaultValue: "126",
                restart: RestartRequirement.SignOut),

            ReadPreference(
                id: "copilot-button",
                displayName: "Hide the Copilot taskbar button",
                description: "Removes the Copilot button from the taskbar. The assistant itself stays reachable unless Windows Copilot is also disabled by policy (requires Explorer restart).",
                keyPath: AnnoyancesRegistryPaths.ExplorerAdvancedKeyPath,
                valueName: "ShowCopilotButton",
                section: AnnoyanceSection.AiFeatures,
                restart: RestartRequirement.ExplorerRestart),

            ReadPreference(
                id: "edge-sidebar",
                displayName: "Disable the Edge sidebar",
                description: "Removes Microsoft Edge's Hubs sidebar (the Copilot/shopping/tools rail on the right edge of the browser) by policy. Takes effect the next time Edge restarts.",
                keyPath: AnnoyancesRegistryPaths.EdgePoliciesKeyPath,
                valueName: "HubsSidebarEnabled",
                section: AnnoyanceSection.AiFeatures),
        ];
    }

    /// <summary>
    /// TurnOffWindowsCopilot in machine AND user policy scope (TWEAKS.md audits both set
    /// together). Surfaced as ONE toggle (a single atomic group), so not in ReadAll.
    /// Polarity is inverted vs the CDM prefs: value present 1 = suppressed, missing = 0.
    /// </summary>
    public IReadOnlyList<AnnoyancePreference> ReadCopilotPolicy()
    {
        return
        [
            ReadPreference(
                id: "copilot",
                displayName: "Windows Copilot (machine policy)",
                description: "TurnOffWindowsCopilot in machine scope.",
                keyPath: AnnoyancesRegistryPaths.CopilotMachinePoliciesKeyPath,
                valueName: "TurnOffWindowsCopilot",
                section: AnnoyanceSection.AiFeatures,
                suppressedValue: "1",
                defaultValue: "0",
                restart: RestartRequirement.ExplorerRestart),
            ReadPreference(
                id: "copilot",
                displayName: "Windows Copilot (user policy)",
                description: "TurnOffWindowsCopilot in user scope.",
                keyPath: AnnoyancesRegistryPaths.CopilotUserPoliciesKeyPath,
                valueName: "TurnOffWindowsCopilot",
                section: AnnoyanceSection.AiFeatures,
                suppressedValue: "1",
                defaultValue: "0",
                restart: RestartRequirement.ExplorerRestart),
        ];
    }

    /// <summary>
    /// The three HKLM WindowsAI policy values behind Recall and AI data analysis.
    /// Surfaced as ONE toggle (a single atomic group), so not in ReadAll. Note the mixed
    /// polarity: AllowRecallEnablement suppresses at 0, the other two at 1.
    /// </summary>
    public IReadOnlyList<AnnoyancePreference> ReadRecall()
    {
        var windowsAi = AnnoyancesRegistryPaths.WindowsAiPoliciesKeyPath;
        return
        [
            ReadPreference(
                id: "recall",
                displayName: "Recall enablement",
                description: "AllowRecallEnablement policy (0 blocks Recall).",
                keyPath: windowsAi,
                valueName: "AllowRecallEnablement",
                section: AnnoyanceSection.AiFeatures,
                suppressedValue: "0",
                defaultValue: "1"),
            ReadPreference(
                id: "recall",
                displayName: "AI data analysis",
                description: "DisableAIDataAnalysis policy (1 blocks analysis).",
                keyPath: windowsAi,
                valueName: "DisableAIDataAnalysis",
                section: AnnoyanceSection.AiFeatures,
                suppressedValue: "1",
                defaultValue: "0"),
            ReadPreference(
                id: "recall",
                displayName: "Snapshot saving",
                description: "TurnOffSavingSnapshots policy (1 blocks snapshots).",
                keyPath: windowsAi,
                valueName: "TurnOffSavingSnapshots",
                section: AnnoyanceSection.AiFeatures,
                suppressedValue: "1",
                defaultValue: "0"),
        ];
    }

    /// <summary>
    /// The three ContentDeliveryManager values behind "Suggested content in the Settings
    /// app". Surfaced as ONE toggle (a single atomic group), so they are not in ReadAll.
    /// </summary>
    public IReadOnlyList<AnnoyancePreference> ReadSettingsSuggestedContent()
    {
        var cdm = AnnoyancesRegistryPaths.ContentDeliveryManagerKeyPath;
        return
        [
            ReadPreference(
                id: "settings-suggested-content",
                displayName: "Suggested content in Settings (338393)",
                description: "Suggested content entry (SubscribedContent-338393).",
                keyPath: cdm,
                valueName: "SubscribedContent-338393Enabled",
                section: AnnoyanceSection.AdvertisingAndTracking),
            ReadPreference(
                id: "settings-suggested-content",
                displayName: "Suggested content in Settings (353694)",
                description: "Suggested content entry (SubscribedContent-353694).",
                keyPath: cdm,
                valueName: "SubscribedContent-353694Enabled",
                section: AnnoyanceSection.AdvertisingAndTracking),
            ReadPreference(
                id: "settings-suggested-content",
                displayName: "Suggested content in Settings (353696)",
                description: "Suggested content entry (SubscribedContent-353696).",
                keyPath: cdm,
                valueName: "SubscribedContent-353696Enabled",
                section: AnnoyanceSection.AdvertisingAndTracking),
        ];
    }

    public BingSearchState ReadBingSearch()
    {
        // Opposite polarities: BingSearchEnabled 0 = suppressed (missing = 1, active);
        // DisableSearchBoxSuggestions 1 = suppressed (missing policy = 0, allowed).
        var bing = _registryService.ReadDWord(AnnoyancesRegistryPaths.SearchKeyPath, "BingSearchEnabled");
        var bingValue = bing.IsSuccess ? bing.Value!.ToString() : "1";

        var suggestions = _registryService.ReadDWord(
            AnnoyancesRegistryPaths.ExplorerPoliciesKeyPath, "DisableSearchBoxSuggestions");
        var suggestionsValue = suggestions.IsSuccess ? suggestions.Value!.ToString() : "0";

        return new BingSearchState(
            BingSearchEnabledValue: bingValue,
            DisableSearchBoxSuggestionsValue: suggestionsValue,
            IsSuppressed: bingValue == "0" && suggestionsValue == "1");
    }

    private AnnoyancePreference ReadEdgeShortcutPreference()
    {
        // HKLM policy; missing value means Edge Update creates shortcuts (default "1").
        const string keyPath = AnnoyancesRegistryPaths.EdgeUpdatePoliciesKeyPath;
        const string valueName = "CreateDesktopShortcutDefault";

        var result = _registryService.ReadDWord(keyPath, valueName);
        var currentValue = result.IsSuccess ? result.Value!.ToString() : "1";

        return new AnnoyancePreference(
            Id: "edge-shortcuts",
            DisplayName: "Block Edge desktop shortcut creation",
            Description: "Stops Microsoft Edge from recreating its desktop shortcut every time it updates in the background. Major Windows updates are known to overwrite this policy.",
            Section: AnnoyanceSection.BingAndEdge,
            RegistryKeyPath: keyPath,
            RegistryValueName: valueName,
            ValueType: ChangeValueType.Registry_DWord,
            CurrentValue: currentValue,
            SuppressedValue: "0",
            DefaultValue: "1",
            IsSuppressed: currentValue == "0",
            RestartRequirement: RestartRequirement.None);
    }

    private AnnoyancePreference ReadPreference(
        string id, string displayName, string description, string keyPath, string valueName,
        AnnoyanceSection section = AnnoyanceSection.ScoobeAndWelcome,
        ChangeValueType valueType = ChangeValueType.Registry_DWord,
        string suppressedValue = "0",
        string defaultValue = "1",
        RestartRequirement restart = RestartRequirement.None)
    {
        // Default shape: DWORD, 1 = annoyance active (the Windows default when the value
        // is missing), 0 = suppressed. HAGS (1/2) and the accessibility Flags strings
        // override the value type and pair.
        string currentValue;
        if (valueType == ChangeValueType.Registry_String)
        {
            var read = _registryService.ReadString(keyPath, valueName);
            currentValue = read.IsSuccess ? read.Value! : defaultValue;
        }
        else
        {
            var read = _registryService.ReadDWord(keyPath, valueName);
            currentValue = read.IsSuccess ? read.Value!.ToString() : defaultValue;
        }

        return new AnnoyancePreference(
            Id: id,
            DisplayName: displayName,
            Description: description,
            Section: section,
            RegistryKeyPath: keyPath,
            RegistryValueName: valueName,
            ValueType: valueType,
            CurrentValue: currentValue,
            SuppressedValue: suppressedValue,
            DefaultValue: defaultValue,
            IsSuppressed: currentValue == suppressedValue,
            RestartRequirement: restart);
    }
}
