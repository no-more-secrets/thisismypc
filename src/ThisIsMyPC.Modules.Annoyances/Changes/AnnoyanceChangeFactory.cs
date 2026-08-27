using ThisIsMyPC.Core.Changes;
using ThisIsMyPC.Core.Enforcement;
using ThisIsMyPC.Modules.Annoyances.Models;

namespace ThisIsMyPC.Modules.Annoyances.Changes;

public static class AnnoyanceChangeFactory
{
    public const string ModuleId = "Windows Annoyances";

    // Research-documented drift (control-surface research L244): Windows Update and Web
    // Experience Pack deployments overwrite these keys. Informational only — no companions.
    private static readonly SettingEnforcement DriftFragileEnforcement = new()
    {
        ReversionVectors = ["Windows Update", "Web Experience Pack deployment"],
    };

    /// <summary>
    /// Creates a toggle change. <paramref name="suppress"/> true writes the suppressing
    /// value; false restores the Windows default. BeforeValue is the preference's live
    /// CurrentValue (a missing registry value scans as the default), preserving true
    /// before-state fidelity for revert. SettingEnforcement stays null per FR139.
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
