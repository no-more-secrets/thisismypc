using ThisIsMyPC.Core.Changes;
using ThisIsMyPC.Core.Enforcement;
using ThisIsMyPC.Modules.Privacy.Models;

namespace ThisIsMyPC.Modules.Privacy.Changes;

public static class PrivacyChangeFactory
{
    public const string ModuleId = "Privacy & Telemetry";

    // DiagTrack is disabled alongside the telemetry policy (the executor stops and
    // disables it with rollback on apply). Restore asymmetry, known and deliberate:
    // only History UNDO re-enables DiagTrack (it reverts the original configure
    // descriptor through EnforcementExecutor.RevertAsync); toggling the card back
    // off stages a restore descriptor with null Enforcement, which deletes the
    // policy but leaves DiagTrack disabled — the executor cannot express
    // "re-enable companion on staged apply" today (its companion semantics are
    // always disable-with-rollback). The card description states this; the proper
    // fix (directional companions or a cross-module DiagTrack start-type change)
    // is a backlog design decision.
    // AllowTelemetry is one of the few policies whose CSP table includes Home — no
    // SKU tag.
    internal static readonly SettingEnforcement TelemetryEnforcement = new()
    {
        CompanionServices = ["DiagTrack"],
        ReversionVectors = ["Windows feature updates"],
    };

    // Minimum-tier tags (informational, never gated — FR129): LocationAndSensors and
    // TabletPC policies list Pro/Enterprise/Education in the Policy CSP.
    internal static readonly SettingEnforcement ProPolicyEnforcement = new()
    {
        SkuRestriction = Core.Modules.WindowsSku.Pro,
    };

    // Per-id lookup so every staging path (cards, set entries) inherits the
    // enforcement from this single factory entry point. Attached on the configure
    // direction only (26-4 rule).
    private static readonly IReadOnlyDictionary<string, SettingEnforcement> ConfigureEnforcement =
        new Dictionary<string, SettingEnforcement>(StringComparer.Ordinal)
        {
            ["telemetry-level"] = TelemetryEnforcement,
            ["location"] = ProPolicyEnforcement,
            ["handwriting-data-sharing"] = ProPolicyEnforcement,
        };

    /// <summary>
    /// Creates a toggle change. <paramref name="configure"/> true writes the
    /// privacy-hardened value; false restores <see cref="PrivacyPreference.DefaultValue"/>
    /// (empty = delete the value, the WU/Power convention). BeforeValue is the live
    /// CurrentValue for true before-state fidelity.
    /// </summary>
    public static ChangeDescriptor CreateToggle(PrivacyPreference pref, bool configure)
    {
        return new ChangeDescriptor
        {
            ModuleId = ModuleId,
            SettingId = pref.Id,
            DisplayName = pref.DisplayName,
            SystemLocation = $@"{pref.RegistryKeyPath}\{pref.RegistryValueName}",
            BeforeValue = pref.CurrentValue,
            AfterValue = configure ? pref.ConfiguredValue : pref.DefaultValue,
            BeforeDisplay = pref.IsConfigured ? "Configured" : "Windows default",
            AfterDisplay = configure ? "Configured" : "Windows default",
            ValueType = pref.ValueType,
            Category = ChangeCategory.Modify,
            RestartRequirement = RestartRequirement.None,
            Enforcement = configure && ConfigureEnforcement.TryGetValue(pref.Id, out var enforcement)
                ? enforcement
                : null,
        };
    }

    /// <summary>
    /// One atomic ChangeGroup for the four inking/typing personalization values
    /// (mixed polarity: each descriptor uses its own configured/default pair).
    /// </summary>
    public static ChangeGroup CreateInkingTypingGroup(
        IReadOnlyList<PrivacyPreference> prefs, bool configure, string description)
    {
        return new ChangeGroup
        {
            GroupId = Guid.NewGuid().ToString("N"),
            DisplayName = "Inking and typing personalization",
            Description = description,
            Changes = prefs.Select(pref => CreateToggle(pref, configure)).ToList(),
        };
    }
}
