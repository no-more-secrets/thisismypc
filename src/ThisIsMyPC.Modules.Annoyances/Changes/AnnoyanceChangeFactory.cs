using ThisIsMyPC.Core.Changes;
using ThisIsMyPC.Core.Enforcement;
using ThisIsMyPC.Modules.Annoyances.Models;

namespace ThisIsMyPC.Modules.Annoyances.Changes;

public static class AnnoyanceChangeFactory
{
    public const string ModuleId = "Windows Annoyances";

    // Research-documented drift (control-surface research L244): Windows Update and Web
    // Experience Pack deployments overwrite these keys. Informational only; no companions.
    private static readonly SettingEnforcement DriftFragileEnforcement = new()
    {
        ReversionVectors = ["Windows Update", "Web Experience Pack deployment"],
    };

    // Copilot surfaces are re-enabled by feature updates and by Copilot's own app-package
    // deployments; the policy pair is otherwise stable. Informational only; no companions.
    // SkuRestriction (minimum honoring tier): TurnOffWindowsCopilot lists
    // Pro/Enterprise/Education in the Policy CSP (docs/research/sku-restriction-audit.md).
    private static readonly SettingEnforcement CopilotDriftEnforcement = new()
    {
        ReversionVectors = ["Windows feature updates", "Copilot app deployment"],
        SkuRestriction = Core.Modules.WindowsSku.Pro,
    };

    // Minimum-tier policy tags (informational, never gated; FR129), attached on the
    // suppress direction only (26-4 rule); source: docs/research/sku-restriction-audit.md
    // + the Experience Policy CSP for DisableSpotlightCollectionOnDesktop
    // (Enterprise/Education only; below that tier the write is cosmetic).
    private static readonly SettingEnforcement ProPolicyEnforcement = new()
    {
        SkuRestriction = Core.Modules.WindowsSku.Pro,
    };

    private static readonly SettingEnforcement EducationPolicyEnforcement = new()
    {
        SkuRestriction = Core.Modules.WindowsSku.Education,
    };

    // Per-id lookup so every staging path (module UI cards, set entries) inherits the
    // tag from the single factory entry point. Only plain-CreateToggle singles belong
    // here; CreateDriftFragileToggle and CreateGroupToggle overwrite Enforcement
    // wholesale, so an id routed through those paths would silently lose the tag.
    private static readonly IReadOnlyDictionary<string, SettingEnforcement> TierRestrictedSingles =
        new Dictionary<string, SettingEnforcement>(StringComparer.Ordinal)
        {
            ["spotlight-collection-desktop"] = EducationPolicyEnforcement,
            ["consumer-features"] = EducationPolicyEnforcement,
        };

    /// <summary>
    /// Creates a toggle change. <paramref name="suppress"/> true writes the suppressing
    /// value; false restores the Windows default. BeforeValue is the preference's live
    /// CurrentValue (a missing registry value scans as the default), preserving true
    /// before-state fidelity for revert. SettingEnforcement stays null per FR139,
    /// except TierRestrictedSingles ids, which carry the minimum-tier tag on suppress (26-9).
    /// </summary>
    public static ChangeDescriptor CreateToggle(AnnoyancePreference pref, bool suppress)
    {
        return new ChangeDescriptor
        {
            ModuleId = ModuleId,
            SettingId = pref.Id,
            DisplayName = pref.DisplayName,
            SystemLocation = $@"{pref.RegistryKeyPath}\{pref.RegistryValueName}",
            BeforeValue = pref.CurrentValue,
            AfterValue = suppress ? pref.SuppressedValue : pref.DefaultValue,
            BeforeDisplay = pref.IsSuppressed ? "Suppressed" : "Windows default",
            AfterDisplay = suppress ? "Suppressed" : "Windows default",
            ValueType = pref.ValueType,
            Category = suppress ? ChangeCategory.Disable : ChangeCategory.Enable,
            RestartRequirement = pref.RestartRequirement,
            Enforcement = suppress && TierRestrictedSingles.TryGetValue(pref.Id, out var tierEnforcement)
                ? tierEnforcement
                : null,
        };
    }

    /// <summary>
    /// Drift-fragile variant of <see cref="CreateToggle"/>: attaches the Windows Update /
    /// Web Experience Pack reversion vectors on the suppress direction (26-4 rule).
    /// </summary>
    public static ChangeDescriptor CreateDriftFragileToggle(AnnoyancePreference pref, bool suppress)
        => CreateToggle(pref, suppress) with
        {
            Enforcement = suppress ? DriftFragileEnforcement : null,
        };

    /// <summary>
    /// One atomic ChangeGroup from several preferences surfaced as a single toggle
    /// (e.g. the three Settings suggested-content values). Each descriptor uses its own
    /// preference's suppressed/default pair, so mixed-polarity groups work. All
    /// descriptors share <paramref name="settingId"/>; enforcement stays null unless
    /// <paramref name="suppressEnforcement"/> is given (attached on suppress only,
    /// 26-4 rule).
    /// </summary>
    public static ChangeGroup CreateGroupToggle(
        IReadOnlyList<AnnoyancePreference> prefs,
        string settingId,
        string displayName,
        string description,
        bool suppress,
        SettingEnforcement? suppressEnforcement = null)
    {
        return new ChangeGroup
        {
            GroupId = Guid.NewGuid().ToString("N"),
            DisplayName = displayName,
            Description = description,
            // Shared SettingId groups them; per-pref DisplayName keeps each review-panel
            // row self-describing (rows otherwise differ only by SystemLocation).
            Changes = prefs.Select(pref => CreateToggle(pref, suppress) with
            {
                SettingId = settingId,
                Enforcement = suppress ? suppressEnforcement : null,
            }).ToList(),
        };
    }

    /// <summary>
    /// One atomic ChangeGroup turning Windows Copilot off (or back on) in machine and
    /// user policy scope together, with the Copilot drift reversion vectors on suppress.
    /// </summary>
    public static ChangeGroup CreateCopilotPolicyToggle(
        IReadOnlyList<AnnoyancePreference> prefs, bool suppress)
        => CreateGroupToggle(
            prefs,
            settingId: "copilot",
            displayName: "Windows Copilot",
            description: "Disables the Windows Copilot assistant by policy in both machine and user scope.",
            suppress,
            suppressEnforcement: CopilotDriftEnforcement);

    /// <summary>
    /// One atomic ChangeGroup for the activity-history System policy trio, with the
    /// Pro minimum-tier tag on suppress (the ActivityFeed policies list Pro+ only).
    /// </summary>
    public static ChangeGroup CreateActivityHistoryToggle(
        IReadOnlyList<AnnoyancePreference> prefs, bool suppress, string description)
        => CreateGroupToggle(
            prefs,
            settingId: "activity-history",
            displayName: "Activity history",
            description,
            suppress,
            suppressEnforcement: ProPolicyEnforcement);

    /// <summary>
    /// One atomic ChangeGroup for the Recall/WindowsAI policy trio, with the Pro
    /// minimum-tier tag on suppress (the WindowsAI policies list Pro+ only).
    /// </summary>
    public static ChangeGroup CreateRecallPolicyToggle(
        IReadOnlyList<AnnoyancePreference> prefs, bool suppress, string description)
        => CreateGroupToggle(
            prefs,
            settingId: "recall",
            displayName: "Windows Recall and AI data analysis",
            description,
            suppress,
            suppressEnforcement: ProPolicyEnforcement);

    /// <summary>
    /// One atomic ChangeGroup for Bing search: BingSearchEnabled → 0 and
    /// DisableSearchBoxSuggestions → 1 when suppressing (opposite polarities), both
    /// restored to Windows defaults when not. Explorer restart required for the Start
    /// Menu search pane to pick the change up.
    /// </summary>
    public static ChangeGroup CreateBingSearchToggle(BingSearchState current, bool suppress)
    {
        var enforcement = suppress ? DriftFragileEnforcement : null;
        var afterDisplay = suppress ? "Suppressed" : "Windows default";

        var bingChange = new ChangeDescriptor
        {
            ModuleId = ModuleId,
            SettingId = "bing-search",
            DisplayName = "Bing web search in Start Menu",
            SystemLocation = $@"{AnnoyancesRegistryPaths.SearchKeyPath}\BingSearchEnabled",
            BeforeValue = current.BingSearchEnabledValue,
            AfterValue = suppress ? "0" : "1",
            // Per-descriptor display: partial states (only one value set) show accurately
            BeforeDisplay = current.BingSearchEnabledValue == "0" ? "Suppressed" : "Windows default",
            AfterDisplay = afterDisplay,
            ValueType = ChangeValueType.Registry_DWord,
            Category = suppress ? ChangeCategory.Disable : ChangeCategory.Enable,
            RestartRequirement = RestartRequirement.ExplorerRestart,
            Enforcement = enforcement,
        };

        var suggestionsChange = new ChangeDescriptor
        {
            ModuleId = ModuleId,
            SettingId = "bing-search",
            DisplayName = "Search box web suggestions",
            SystemLocation = $@"{AnnoyancesRegistryPaths.ExplorerPoliciesKeyPath}\DisableSearchBoxSuggestions",
            BeforeValue = current.DisableSearchBoxSuggestionsValue,
            AfterValue = suppress ? "1" : "0",
            BeforeDisplay = current.DisableSearchBoxSuggestionsValue == "1" ? "Suppressed" : "Windows default",
            AfterDisplay = afterDisplay,
            ValueType = ChangeValueType.Registry_DWord,
            Category = suppress ? ChangeCategory.Disable : ChangeCategory.Enable,
            RestartRequirement = RestartRequirement.ExplorerRestart,
            Enforcement = enforcement,
        };

        return new ChangeGroup
        {
            GroupId = Guid.NewGuid().ToString("N"),
            DisplayName = "Bing web search in Start Menu",
            Description = "Start Menu search will no longer send queries to Bing; results stay local.",
            Changes = [bingChange, suggestionsChange],
        };
    }
}
