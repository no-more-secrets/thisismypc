using ThisIsMyPC.Core.Changes;
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
            case "lock-screen-ads":
                return InspectGroup("Lock screen tips and ads", _reader.ReadLockScreenAds(), entry);
            case "preinstalled-apps":
                return InspectGroup("OEM and preinstalled app promotions", _reader.ReadPreinstalledApps(), entry);
            case "edge-debloat":
                return InspectGroup("Edge shopping, Rewards, and personalization", _reader.ReadEdgeDebloat(), entry);
            case "activity-history":
                return InspectGroup("Activity history", _reader.ReadActivityHistory(), entry);
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

    public ChangeGroup? CreateChangeGroup(SetEntry entry)
    {
        switch (entry.SettingId)
        {
            case "copilot":
            {
                var prefs = _reader.ReadCopilotPolicy();
                return Direction(entry, prefs[0]) is { } suppress
                    ? AnnoyanceChangeFactory.CreateCopilotPolicyToggle(prefs, suppress)
                    : null;
            }
            case "recall":
            {
                var prefs = _reader.ReadRecall();
                return Direction(entry, prefs[0]) is { } suppress
                    ? AnnoyanceChangeFactory.CreateRecallPolicyToggle(
                        prefs,
                        suppress,
                        description: "Blocks Windows Recall snapshots and AI analysis of your activity (three WindowsAI policies set together).")
                    : null;
            }
            case "settings-suggested-content":
            {
                var prefs = _reader.ReadSettingsSuggestedContent();
                return Direction(entry, prefs[0]) is { } suppress
                    ? AnnoyanceChangeFactory.CreateGroupToggle(
                        prefs,
                        settingId: "settings-suggested-content",
                        displayName: "Suggested content in Settings",
                        description: "The ad-like suggested content tiles in the Settings app (three ContentDeliveryManager values set together).",
                        suppress)
                    : null;
            }
            case "lock-screen-ads":
            {
                var prefs = _reader.ReadLockScreenAds();
                return Direction(entry, prefs[0]) is { } suppress
                    ? AnnoyanceChangeFactory.CreateGroupToggle(
                        prefs,
                        settingId: "lock-screen-ads",
                        displayName: "Lock screen tips and ads",
                        description: "Removes tips, fun facts, and ads from the lock screen.",
                        suppress)
                    : null;
            }
            case "preinstalled-apps":
            {
                var prefs = _reader.ReadPreinstalledApps();
                return Direction(entry, prefs[0]) is { } suppress
                    ? AnnoyanceChangeFactory.CreateGroupToggle(
                        prefs,
                        settingId: "preinstalled-apps",
                        displayName: "OEM and preinstalled app promotions",
                        description: "Stops promotions for OEM and preinstalled apps and the related feature tips.",
                        suppress)
                    : null;
            }
            case "edge-debloat":
            {
                var prefs = _reader.ReadEdgeDebloat();
                return Direction(entry, prefs[0]) is { } suppress
                    ? AnnoyanceChangeFactory.CreateGroupToggle(
                        prefs,
                        settingId: "edge-debloat",
                        displayName: "Edge shopping, Rewards, and personalization",
                        description: "Turns off the shopping assistant, Microsoft Rewards, and personalization reporting in Edge.",
                        suppress)
                    : null;
            }
            case "activity-history":
            {
                var prefs = _reader.ReadActivityHistory();
                return Direction(entry, prefs[0]) is { } suppress
                    ? AnnoyanceChangeFactory.CreateActivityHistoryToggle(
                        prefs, suppress,
                        description: "Stops Windows from collecting, publishing, and uploading your activity history.")
                    : null;
            }
            case "bing-search":
                return entry.Value is "0" or "1"
                    ? AnnoyanceChangeFactory.CreateBingSearchToggle(_reader.ReadBingSearch(), suppress: entry.Value == "0")
                    : null;
        }

        var pref = _reader.ReadAll().FirstOrDefault(p => p.Id == entry.SettingId);
        if (pref is null || Direction(entry, pref) is not { } suppressSingle)
            return null;

        // The BingAndEdge section's single toggle (edge-shortcuts) carries the
        // drift-fragile reversion vectors, exactly like the module UI stages it.
        var change = pref.Section == AnnoyanceSection.BingAndEdge
            ? AnnoyanceChangeFactory.CreateDriftFragileToggle(pref, suppressSingle)
            : AnnoyanceChangeFactory.CreateToggle(pref, suppressSingle);

        return new ChangeGroup
        {
            GroupId = Guid.NewGuid().ToString("N"),
            DisplayName = pref.DisplayName,
            Description = pref.Description,
            Changes = [change],
        };
    }

    /// <summary>Maps the entry value to a toggle direction; null = neither direction.</summary>
    private static bool? Direction(SetEntry entry, AnnoyancePreference primary)
        => string.Equals(entry.Value, primary.SuppressedValue, StringComparison.Ordinal) ? true
            : string.Equals(entry.Value, primary.DefaultValue, StringComparison.Ordinal) ? false
            : null;

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
