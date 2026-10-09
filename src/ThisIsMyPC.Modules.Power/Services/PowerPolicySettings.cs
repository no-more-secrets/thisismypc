using ThisIsMyPC.Core.Changes;
using ThisIsMyPC.Core.Enforcement;
using ThisIsMyPC.Core.Modules;
using ThisIsMyPC.Core.Policies;
using ThisIsMyPC.Core.Services;
using ThisIsMyPC.Modules.Power.Changes;

namespace ThisIsMyPC.Modules.Power.Services;

/// <summary>Captures saved and live power policies for reversible edits.</summary>
public sealed class PowerPolicySettings(IRegistryService registry, PolicyControlStateReader policies)
{
    public const string PinLocation = PowerPlanChangeFactory.ActivePlanPolicyKeyPath + "\\" + PowerPlanChangeFactory.ActivePlanPolicyValueName;
    private static readonly string[] SleepLocations =
    [
        PowerPlanChangeFactory.AllowStandbyPolicyKeyPath + "\\" + PowerPlanChangeFactory.PluggedInIndexValueName,
        PowerPlanChangeFactory.AllowStandbyPolicyKeyPath + "\\" + PowerPlanChangeFactory.OnBatteryIndexValueName,
    ];

    public string? UnavailableReason => SettingEditionSupport.BlockReason(new CapabilityDetector(registry).Sku, WindowsSku.Pro);

    private LocalPolicyValue Read(string location, ChangeValueType type, IReadOnlyList<PolicySourceSnapshot> sources)
    {
        var split = location.LastIndexOf('\\');
        var value = LocalPolicyValue.Read(location, type, sources, registry.ReadValue(location[..split], location[(split + 1)..]));
        if (!Valid(value, type)) throw new InvalidOperationException("The power policy value is not recognized.");
        if (value.Saved is not null && !value.Saved.Equals(value.Live, StringComparison.OrdinalIgnoreCase)
            || value.Delete && value.Live.Length > 0)
            throw new InvalidOperationException("Saved policy differs from its current value. Refresh policy before editing.");
        return value;
    }

    public PolicyControlState State(bool sleep)
    {
        try
        {
            if (!policies.HasLocalSourceReader) return new("The saved local policy reader is unavailable.", true);
            var sources = policies.ReadLocalSources();
            // Normal configured states are not errors. The switch already shows the choice.
            foreach (var location in sleep ? SleepLocations : [PinLocation])
                Read(location, sleep ? ChangeValueType.LocalPolicy_DWord : ChangeValueType.LocalPolicy_String, sources);
            return PolicyControlState.None;
        }
        catch (Exception ex) when (ex is InvalidOperationException or IOException or UnauthorizedAccessException
            or System.Security.SecurityException or System.Text.DecoderFallbackException)
        { return new(ex.Message, true); }
    }

    public ChangeGroup CreateSleep(bool allow) => Create(true, allow, null);
    public ChangeGroup CreatePin(Guid plan, bool keep) => Create(false, keep, plan);

    private ChangeGroup Create(bool sleep, bool enabled, Guid? plan)
    {
        if (UnavailableReason is { } reason) throw new InvalidOperationException(reason);
        if (!policies.HasLocalSourceReader) throw new InvalidOperationException("The saved local policy reader is unavailable.");
        var sources = policies.ReadLocalSources();
        var type = sleep ? ChangeValueType.LocalPolicy_DWord : ChangeValueType.LocalPolicy_String;
        var next = sleep ? enabled ? "" : "0" : enabled ? plan!.Value.ToString("D") : "";
        var changes = (sleep ? SleepLocations : [PinLocation]).Select(location =>
        {
            var before = Read(location, type, sources);
            var after = new LocalPolicyValue(next.Length == 0 ? null : next, next);
            return new ChangeDescriptor
            {
                ModuleId = PowerPlanChangeFactory.ModuleId,
                SettingId = sleep ? PowerPlanChangeFactory.AllowSleepSettingId : PowerPlanChangeFactory.ActivePlanPolicyPinSettingId,
                DisplayName = sleep ? location == SleepLocations[0] ? "Sleep when plugged in" : "Sleep on battery" : "Power plan policy pin",
                SystemLocation = location, ValueType = type,
                BeforeValue = before.Encode(), AfterValue = after.Encode(),
                BeforeDisplay = sleep ? before.Live == "0" ? "Blocked by policy" : "Allowed" : before.Live.Length == 0 ? "Not pinned" : "Pinned",
                AfterDisplay = sleep ? enabled ? "Allowed" : "Blocked by policy" : enabled ? "Pinned" : "Not pinned",
                Category = enabled ? ChangeCategory.Enable : ChangeCategory.Disable,
                RestartRequirement = RestartRequirement.Reboot,
                Enforcement = new SettingEnforcement { SkuRestriction = WindowsSku.Pro },
            };
        }).ToList();
        return new() { GroupId = Guid.NewGuid().ToString("N"), DisplayName = sleep ? "Allow sleep" : "Power plan policy pin",
            Description = "Update the saved and current power policy.", Changes = changes };
    }

    private static bool Valid(LocalPolicyValue? value, ChangeValueType type) => type == ChangeValueType.LocalPolicy_DWord
        ? LocalPolicyToggleCatalog.Valid(value)
        : value is not null && (value.Saved is null || Guid.TryParseExact(value.Saved, "D", out _))
            && (value.Live.Length == 0 || Guid.TryParseExact(value.Live, "D", out _));

    public static bool Allows(ChangeDescriptor change) => change.ModuleId == PowerPlanChangeFactory.ModuleId
        && (change.SettingId == PowerPlanChangeFactory.AllowSleepSettingId && change.ValueType == ChangeValueType.LocalPolicy_DWord
                && SleepLocations.Contains(change.SystemLocation, StringComparer.OrdinalIgnoreCase)
            || change.SettingId == PowerPlanChangeFactory.ActivePlanPolicyPinSettingId && change.ValueType == ChangeValueType.LocalPolicy_String
                && change.SystemLocation.Equals(PinLocation, StringComparison.OrdinalIgnoreCase))
        && change.Enforcement is { SkuRestriction: WindowsSku.Pro, AclElevation: false, OwnerModeRequired: false }
        && change.Enforcement.CompanionServices is not { Count: > 0 } && change.Enforcement.CompanionTasks is not { Count: > 0 }
        && change.Enforcement.GPCacheEntries is not { Count: > 0 } && change.Enforcement.ReversionVectors is not { Count: > 0 }
        && Valid(LocalPolicyValue.Decode(change.BeforeValue), change.ValueType) && Valid(LocalPolicyValue.Decode(change.AfterValue), change.ValueType);
}
