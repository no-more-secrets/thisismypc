namespace ThisIsMyPC.Modules.Annoyances.Models;

public sealed record AnnoyancesScanData(
    IReadOnlyList<AnnoyancePreference> Preferences,
    BingSearchState BingSearch,
    IReadOnlyList<AnnoyancePreference> SettingsSuggestedContent,
    IReadOnlyList<AnnoyancePreference> CopilotPolicy,
    IReadOnlyList<AnnoyancePreference> Recall,
    IReadOnlyList<AnnoyancePreference> LockScreenAds,
    IReadOnlyList<AnnoyancePreference> PreinstalledApps,
    IReadOnlyList<AnnoyancePreference> EdgeDebloat);
