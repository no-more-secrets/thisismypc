using ThisIsMyPC.Core.Services;
using ThisIsMyPC.Core.Sets;
using ThisIsMyPC.Modules.Annoyances.Changes;
using ThisIsMyPC.Modules.Annoyances.Models;

namespace ThisIsMyPC.Modules.Annoyances.Services;

/// <summary>
/// Resolves set entries targeting "Windows Annoyances" to live system state. Group
/// toggles (one settingId over several registry values) report Suppressed / Windows
/// default / Partially set; IsApplied is direction-aware per the toggle value convention
/// (SetEntry.Value equals the group's first suppressed value when the entry wants
/// suppression).
/// </summary>
public sealed class AnnoyancesSetEntryInspector : ISetEntryInspector
{
    private readonly AnnoyancesSettingsReader _reader;

    public AnnoyancesSetEntryInspector(IRegistryService registryService)
    {
        _reader = new AnnoyancesSettingsReader(registryService);
    }

    public string ModuleId => AnnoyanceChangeFactory.ModuleId;

    public SetEntryState? Inspect(SetEntry entry)
    {
        switch (entry.SettingId)
        {
            case "copilot":
                return InspectGroup("Windows Copilot", _reader.ReadCopilotPolicy(), entry);
            case "recall":
                return InspectGroup("Windows Recall and AI data analysis", _reader.ReadRecall(), entry);
            case "settings-suggested-content":
                return InspectGroup("Suggested content in Settings", _reader.ReadSettingsSuggestedContent(), entry);
            case "bing-search":
                return InspectBingSearch(entry);
        }

        var pref = _reader.ReadAll().FirstOrDefault(p => p.Id == entry.SettingId);
        if (pref is null)
            return null;

        return new SetEntryState
        {
            SettingDisplayName = pref.DisplayName,
            CurrentValue = pref.CurrentValue,
            CurrentDisplay = pref.IsSuppressed ? "Suppressed" : "Windows default",
            IsApplied = string.Equals(pref.CurrentValue, entry.Value, StringComparison.Ordinal),
        };
    }

    private static SetEntryState InspectGroup(
        string displayName, IReadOnlyList<AnnoyancePreference> prefs, SetEntry entry)
    {
        var wantsSuppression = string.Equals(entry.Value, prefs[0].SuppressedValue, StringComparison.Ordinal);
        var wantsDefault = string.Equals(entry.Value, prefs[0].DefaultValue, StringComparison.Ordinal);
        var suppressedCount = prefs.Count(p => p.IsSuppressed);

        return new SetEntryState
        {
            SettingDisplayName = displayName,
            CurrentValue = prefs[0].CurrentValue,
            CurrentDisplay = suppressedCount == prefs.Count ? "Suppressed"
                : suppressedCount == 0 ? "Windows default"
                : "Partially set",
            // A user-authored value matching neither direction is never "applied" —
            // it must not preview as done on a default machine.
            IsApplied = wantsSuppression ? suppressedCount == prefs.Count
                : wantsDefault && suppressedCount == 0,
        };
    }

    private SetEntryState InspectBingSearch(SetEntry entry)
    {
        var state = _reader.ReadBingSearch();
        // Fully default = both values at their Windows defaults; anything else that is
        // not fully suppressed is a partial state.
        var isDefault = state.BingSearchEnabledValue != "0" && state.DisableSearchBoxSuggestionsValue != "1";
        var wantsSuppression = string.Equals(entry.Value, "0", StringComparison.Ordinal);

        return new SetEntryState
        {
            SettingDisplayName = "Bing web search in Start Menu",
            CurrentValue = state.BingSearchEnabledValue,
            CurrentDisplay = state.IsSuppressed ? "Suppressed"
                : isDefault ? "Windows default"
                : "Partially set",
            IsApplied = wantsSuppression
                ? state.IsSuppressed
                : string.Equals(entry.Value, "1", StringComparison.Ordinal) && isDefault,
        };
    }
}
