using ThisIsMyPC.Core.Changes;
using ThisIsMyPC.Core.Enforcement;
using ThisIsMyPC.Modules.Privacy.Models;

namespace ThisIsMyPC.Modules.Privacy.Changes;

public static class PrivacyChangeFactory
{
    public const string ModuleId = "Privacy & Telemetry";

    // DiagTrack rides along in both directions via directional companions: configure
    // disables it (with rollback), restore re-enables it to Manual
    // (RestoresCompanions). AllowTelemetry is one of the few policies whose CSP
    // table includes Home; no SKU tag.
    internal static readonly SettingEnforcement TelemetryEnforcement = new()
    {
        CompanionServices = ["DiagTrack"],
        ReversionVectors = ["Windows feature updates"],
    };

    internal static readonly SettingEnforcement TelemetryRestoreEnforcement = new()
    {
        CompanionServices = ["DiagTrack"],
        RestoresCompanions = true,
    };

    // WerSvc rides along with the error-reporting policy the same directional way.
    internal static readonly SettingEnforcement ErrorReportingEnforcement = new()
    {
        CompanionServices = ["WerSvc"],
    };

    internal static readonly SettingEnforcement ErrorReportingRestoreEnforcement = new()
    {
        CompanionServices = ["WerSvc"],
        RestoresCompanions = true,
    };

    // Minimum-tier tags (informational, never gated; FR129): LocationAndSensors and
    // TabletPC policies list Pro/Enterprise/Education in the Policy CSP.
    internal static readonly SettingEnforcement ProPolicyEnforcement = new()
    {
        SkuRestriction = Core.Modules.WindowsSku.Pro,
    };

    // Per-id lookups so every staging path (cards, set entries) inherits the
    // enforcement from this single factory entry point. Informational tags attach on
    // configure only (26-4 rule); telemetry additionally carries a restore-direction
    // enforcement so DiagTrack is re-enabled when the toggle goes back off.
    private static readonly IReadOnlyDictionary<string, SettingEnforcement> ConfigureEnforcement =
        new Dictionary<string, SettingEnforcement>(StringComparer.Ordinal)
        {
            ["telemetry-level"] = TelemetryEnforcement,
            ["error-reporting"] = ErrorReportingEnforcement,
            ["location"] = ProPolicyEnforcement,
            ["cross-device-clipboard"] = ProPolicyEnforcement,
            ["handwriting-data-sharing"] = ProPolicyEnforcement,
        };

    private static readonly IReadOnlyDictionary<string, SettingEnforcement> RestoreEnforcement =
        new Dictionary<string, SettingEnforcement>(StringComparer.Ordinal)
        {
            ["telemetry-level"] = TelemetryRestoreEnforcement,
            ["error-reporting"] = ErrorReportingRestoreEnforcement,
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
            Enforcement = (configure ? ConfigureEnforcement : RestoreEnforcement)
                .TryGetValue(pref.Id, out var enforcement) ? enforcement : null,
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
