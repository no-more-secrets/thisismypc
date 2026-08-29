using ThisIsMyPC.Core.Changes;
using ThisIsMyPC.Core.Enforcement;
using ThisIsMyPC.Core.Modules;
using ThisIsMyPC.Modules.WindowsUpdate.Models;

namespace ThisIsMyPC.Modules.WindowsUpdate.Changes;

public static class WindowsUpdateChangeFactory
{
    public const string ModuleId = "Windows Update";

    /// <summary>
    /// WU orchestrator policies: the GPCache overrides the policy hive, so the cache is
    /// cleared around EVERY mutation — including restores to Not configured, where a
    /// stale cache would keep the removed policy alive (unlike the Annoyances
    /// suppress-only rule, which covers informational vectors).
    /// </summary>
    // SkuRestriction is the minimum edition tier that honors the policy: the official
    // Policy CSP edition tables list Pro/Enterprise/Education for every policy this
    // module writes (Update CSP + DODownloadMode) — informational tag, never gated
    // (FR129). Source: docs/research/sku-restriction-audit.md.
    internal static readonly SettingEnforcement WUPolicyEnforcement = new()
    {
        GPCacheEntries = [WindowsUpdateRegistryPaths.GPCacheKeyPath],
        ReversionVectors = ["Group Policy refresh", "Windows feature updates"],
        SkuRestriction = WindowsSku.Pro,
    };

    // Delivery Optimization is read by DoSvc directly (no GPCache), but its policy is
    // also Pro+ only — SKU-only enforcement carries the tag into the set preview.
    internal static readonly SettingEnforcement DOPolicyEnforcement = new()
    {
        SkuRestriction = WindowsSku.Pro,
    };

    /// <summary>
    /// Creates a policy toggle change. <paramref name="configure"/> true writes
    /// <see cref="UpdatePolicySetting.ConfiguredValue"/>; false deletes the value
    /// (empty AfterValue = value absent, the PowerModule convention).
    /// <paramref name="gpCache"/> selects the enforcement: true for orchestrator-read
    /// policies (GPCache clear + vectors), false for Delivery Optimization (SKU-only
    /// tag — DoSvc reads its policy key directly).
    /// </summary>
    public static ChangeDescriptor CreateToggle(UpdatePolicySetting setting, bool configure, bool gpCache = true)
    {
        return new ChangeDescriptor
        {
            ModuleId = ModuleId,
            SettingId = setting.Id,
            DisplayName = setting.DisplayName,
            SystemLocation = $@"{setting.RegistryKeyPath}\{setting.RegistryValueName}",
            BeforeValue = setting.CurrentValue,
            AfterValue = configure ? setting.ConfiguredValue : string.Empty,
            BeforeDisplay = setting.IsConfigured ? "Configured" : "Not configured",
            AfterDisplay = configure ? "Configured" : "Not configured",
            ValueType = setting.ValueType,
            Category = ChangeCategory.Modify,
            RestartRequirement = RestartRequirement.None,
            Enforcement = gpCache ? WUPolicyEnforcement : DOPolicyEnforcement,
        };
    }

    /// <summary>
    /// Toggle for a UX\Settings state value (what the Settings page writes): no
    /// enforcement at all — not a policy, so no GPCache clear and no SKU tag.
    /// </summary>
    public static ChangeDescriptor CreateUxToggle(UpdatePolicySetting setting, bool configure)
        => CreateToggle(setting, configure) with { Enforcement = null };

    /// <summary>
    /// One atomic ChangeGroup pinning (or unpinning) the Windows feature release: the
    /// three WindowsUpdate policy values together. Null when the pin is unavailable
    /// (empty group = DisplayVersion unreadable at scan time).
    /// </summary>
    public static ChangeGroup? CreateVersionPinGroup(IReadOnlyList<UpdatePolicySetting> versionPin, bool configure)
    {
        if (versionPin.Count == 0)
            return null;

        return new ChangeGroup
        {
            GroupId = Guid.NewGuid().ToString("N"),
            DisplayName = "Stay on the current Windows version",
            Description = versionPin[0].Description,
            Changes = versionPin.Select(setting => CreateToggle(setting, configure)).ToList(),
        };
    }
}
