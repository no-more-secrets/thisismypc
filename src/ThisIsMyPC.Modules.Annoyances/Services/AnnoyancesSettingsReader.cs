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
        string id, string displayName, string description, string keyPath, string valueName)
    {
        // All Story 27-1 annoyances share the same shape: DWORD, 1 = annoyance active
        // (the Windows default when the value is missing), 0 = suppressed.
        const string suppressedValue = "0";
        const string defaultValue = "1";

        var result = _registryService.ReadDWord(keyPath, valueName);
        var currentValue = result.IsSuccess ? result.Value!.ToString() : defaultValue;

        return new AnnoyancePreference(
            Id: id,
            DisplayName: displayName,
            Description: description,
            Section: AnnoyanceSection.ScoobeAndWelcome,
            RegistryKeyPath: keyPath,
            RegistryValueName: valueName,
            ValueType: ChangeValueType.Registry_DWord,
            CurrentValue: currentValue,
            SuppressedValue: suppressedValue,
            DefaultValue: defaultValue,
            IsSuppressed: currentValue == suppressedValue,
            RestartRequirement: RestartRequirement.None);
    }
}
