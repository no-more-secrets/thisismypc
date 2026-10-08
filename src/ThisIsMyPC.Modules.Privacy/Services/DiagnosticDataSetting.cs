using ThisIsMyPC.Core.Cards;
using ThisIsMyPC.Core.Changes;
using ThisIsMyPC.Core.Enforcement;
using ThisIsMyPC.Core.Modules;
using ThisIsMyPC.Core.Policies;
using ThisIsMyPC.Core.Results;
using ThisIsMyPC.Core.Services;
using ThisIsMyPC.Modules.Privacy.Changes;

namespace ThisIsMyPC.Modules.Privacy.Services;

/// <summary>Windows 11 diagnostic-data policy, with exact saved and live before-state.</summary>
public sealed class DiagnosticDataSetting(IRegistryService registry, ICapabilityDetector? capabilities = null,
    PolicyControlStateReader? policies = null)
{
    public const string Location = PrivacyRegistryPaths.DataCollectionPoliciesKeyPath + "\\AllowTelemetry";
    public static string Display(string value) => value switch
    {
        "" => "Not configured", "0" => "Off", "1" => "Required", "3" => "Optional", _ => "Unknown policy value",
    };

    public IReadOnlyList<SettingOption> Options => (capabilities?.Sku is WindowsSku.Enterprise or WindowsSku.Education
        ? new[] { "", "0", "1", "3" } : new[] { "", "1", "3" })
        .Select(value => new SettingOption(value, Display(value))).ToArray();

    private LocalPolicyValue Read()
    {
        VerifyUserPolicy(registry, policies);
        var live = registry.ReadValue(PrivacyRegistryPaths.DataCollectionPoliciesKeyPath, "AllowTelemetry");
        var value = policies?.HasLocalSourceReader == true
            ? LocalPolicyValue.Read(Location, ChangeValueType.LocalPolicy_DWord, policies.ReadLocalSources(), live)
            : live.IsSuccess && live.Value!.Kind == RegistryValueDataKind.DWord ? new(null, live.Value.Data)
            : !live.IsSuccess && live.ErrorCategory == ErrorCategory.NotFound ? new LocalPolicyValue(null, "")
            : throw new InvalidOperationException("Diagnostic data policy could not be read.");
        if (!Valid(value)) throw new InvalidOperationException("Diagnostic data policy value is not recognized.");
        if (value.Saved is not null && value.Saved != value.Live || value.Delete && value.Live != "")
            throw new InvalidOperationException("Saved policy differs from its current value. Refresh policy before editing.");
        return value;
    }

    internal static void VerifyUserPolicy(IRegistryService registry, PolicyControlStateReader? policies)
    {
        var userLocation = "HKCU" + Location[4..];
        var state = (policies ?? new PolicyControlStateReader(registry)).Read("", "", userLocation, ChangeValueType.Registry_DWord);
        if (state.Message is not null)
            throw new InvalidOperationException("A user policy also controls diagnostic data. " + state.Message);
    }

    private PolicyControlState State()
    {
        try
        {
            var value = Read();
            return new((value.Saved is not null || value.Delete ? "Saved local policy. " : "")
                + "Current policy value: " + (value.Live == "" ? "not configured" : value.Live) + ".");
        }
        catch (Exception ex) when (ex is InvalidOperationException or IOException or UnauthorizedAccessException
            or System.Security.SecurityException or System.Text.DecoderFallbackException)
        { return new(ex.Message, true); }
    }

    public SettingCardSource CreateCard()
    {
        var state = State();
        var value = state.BlocksChanges ? "unknown" : Read().Live;
        return new()
        {
            Model = new()
            {
                ModuleId = PrivacyChangeFactory.ModuleId, SettingId = "telemetry-level", DisplayName = "Diagnostic data",
                Description = "Choose diagnostic data sent to Microsoft. Off requires Enterprise or Education. Not configured restores Windows control. This changes policy, not the telemetry service.",
                ControlType = SettingControlType.Dropdown, CurrentValue = value, CurrentDisplayValue = Display(value),
                AvailableOptions = Options, RegistryPath = PrivacyRegistryPaths.DataCollectionPoliciesKeyPath,
                ValueName = "AllowTelemetry", RegistryValueType = nameof(ChangeValueType.Registry_DWord),
                GroupId = "Diagnostic Data", SkuRestriction = WindowsSku.Pro,
            },
            ReadCurrentState = () => Read().Live == "1",
            CreateToggleGroup = _ => throw new InvalidOperationException("Choose a diagnostic data option."),
            ReadCurrentValue = () => Read().Live, CreateChoiceGroup = Create, ReadPolicyState = State,
        };
    }

    public ChangeGroup Create(string value)
    {
        if (!Options.Any(option => option.Value == value)) throw new ArgumentException("Unsupported diagnostic data option.", nameof(value));
        var reason = SettingEditionSupport.BlockReason(capabilities?.Sku, Location, WindowsSku.Pro, value);
        if (reason is not null) throw new InvalidOperationException(reason);
        var before = Read();
        var local = policies?.HasLocalSourceReader == true;
        return new()
        {
            GroupId = Guid.NewGuid().ToString("N"), DisplayName = "Diagnostic data", Description = "Choose diagnostic data sent to Microsoft.",
            Changes = [new()
            {
                ModuleId = PrivacyChangeFactory.ModuleId, SettingId = "telemetry-level", DisplayName = "Diagnostic data",
                SystemLocation = Location, ValueType = local ? ChangeValueType.LocalPolicy_DWord : ChangeValueType.Registry_DWord,
                BeforeValue = local ? before.Encode() : before.Live,
                AfterValue = local ? new LocalPolicyValue(value == "" ? null : value, value, value == "").Encode() : value,
                BeforeDisplay = Display(before.Live), AfterDisplay = Display(value), Category = ChangeCategory.Modify,
                Enforcement = new SettingEnforcement { SkuRestriction = value == "0" ? WindowsSku.Enterprise : WindowsSku.Pro },
            }],
        };
    }

    private static bool Valid(LocalPolicyValue? value) => value is not null
        && value.Live is "" or "0" or "1" or "3" && value.Saved is null or "0" or "1" or "3";

    public static bool Allows(ChangeDescriptor change) => change.ModuleId == PrivacyChangeFactory.ModuleId
        && change.SettingId == "telemetry-level" && change.SystemLocation.Equals(Location, StringComparison.OrdinalIgnoreCase)
        && change.ValueType == ChangeValueType.LocalPolicy_DWord
        && change.Enforcement is { SkuRestriction: WindowsSku.Pro or WindowsSku.Enterprise, AclElevation: false, OwnerModeRequired: false }
        && change.Enforcement.CompanionServices is not { Count: > 0 } && change.Enforcement.CompanionTasks is not { Count: > 0 }
        && change.Enforcement.GPCacheEntries is not { Count: > 0 } && change.Enforcement.ReversionVectors is not { Count: > 0 }
        && Valid(LocalPolicyValue.Decode(change.BeforeValue)) && Valid(LocalPolicyValue.Decode(change.AfterValue));
}
