using ThisIsMyPC.Core.Cards;
using ThisIsMyPC.Core.Changes;
using ThisIsMyPC.Core.Enforcement;
using ThisIsMyPC.Core.Modules;
using ThisIsMyPC.Core.Policies;
using ThisIsMyPC.Core.Results;
using ThisIsMyPC.Core.Services;

namespace ThisIsMyPC.Modules.WindowsUpdate.Services;

/// <summary>Edits automatic updates without replacing the user's download or installation schedule.</summary>
public sealed class AutomaticUpdatesSetting(IRegistryService registry, ICapabilityDetector? capabilities = null,
    PolicyControlStateReader? policies = null)
{
    public const string Id = "automatic-updates";
    public const string Location = WindowsUpdateRegistryPaths.AuPoliciesKeyPath + "\\NoAutoUpdate";
    public static string Display(string value) => value == "1" ? "Disabled" : value is "" or "0" ? "Enabled" : "Unknown";
    private static readonly SettingOption[] Options = [new("0", "Enabled"), new("1", "Disabled")];

    private LocalPolicyValue Read()
    {
        var live = registry.ReadValue(WindowsUpdateRegistryPaths.AuPoliciesKeyPath, "NoAutoUpdate");
        var value = policies?.HasLocalSourceReader == true
            ? LocalPolicyValue.Read(Location, ChangeValueType.LocalPolicy_DWord, policies.ReadLocalSources(), live)
            : live.IsSuccess && live.Value!.Kind == RegistryValueDataKind.DWord ? new(null, live.Value.Data)
            : !live.IsSuccess && live.ErrorCategory == ErrorCategory.NotFound ? new LocalPolicyValue(null, "")
            : throw new InvalidOperationException("Automatic updates policy could not be read.");
        if (!Valid(value)) throw new InvalidOperationException("Automatic updates policy value is not recognized.");
        if (value.Saved is not null && value.Saved != value.Live || value.Delete && value.Live != "")
            throw new InvalidOperationException("Saved policy differs from its current value. Refresh policy before editing.");
        return value;
    }

    private PolicyControlState State()
    {
        try
        {
            var value = Read();
            return new(value.Saved is not null || value.Delete ? "Saved local policy." : null);
        }
        catch (Exception ex) when (ex is InvalidOperationException or IOException or UnauthorizedAccessException
            or System.Security.SecurityException or System.Text.DecoderFallbackException)
        { return new(ex.Message, true); }
    }

    public SettingCardSource CreateCard()
    {
        var state = State();
        var value = state.BlocksChanges ? "unknown" : Normalize(Read().Live);
        return new()
        {
            Model = new()
            {
                ModuleId = "Windows Update", SettingId = Id, DisplayName = "Automatic Updates",
                Description = "Disabled keeps updates manual. Enabled uses your configured update schedule.",
                ControlType = SettingControlType.Dropdown, CurrentValue = value, CurrentDisplayValue = Display(value),
                AvailableOptions = Options, RegistryPath = WindowsUpdateRegistryPaths.AuPoliciesKeyPath,
                ValueName = "NoAutoUpdate", RegistryValueType = nameof(ChangeValueType.Registry_DWord),
                GroupId = "Update Behavior", SkuRestriction = WindowsSku.Pro,
            },
            ReadCurrentState = () => Read().Live != "1",
            CreateToggleGroup = _ => throw new InvalidOperationException("Choose Enabled or Disabled."),
            ReadCurrentValue = () => Normalize(Read().Live), CreateChoiceGroup = Create, ReadPolicyState = State,
        };
    }

    public ChangeGroup Create(string value)
    {
        if (value is not ("0" or "1")) throw new ArgumentException("Choose Enabled or Disabled.", nameof(value));
        var reason = SettingEditionSupport.BlockReason(capabilities?.Sku, WindowsSku.Pro);
        if (reason is not null) throw new InvalidOperationException(reason);
        if (policies?.HasLocalSourceReader != true) throw new InvalidOperationException("The saved local policy reader is unavailable.");
        var before = Read();
        return new()
        {
            GroupId = Guid.NewGuid().ToString("N"), DisplayName = "Automatic Updates", Description = "Control automatic Windows updates.",
            Changes = [new()
            {
                ModuleId = "Windows Update", SettingId = Id, DisplayName = "Automatic Updates",
                SystemLocation = Location, ValueType = ChangeValueType.LocalPolicy_DWord,
                BeforeValue = before.Encode(), AfterValue = new LocalPolicyValue(value, value).Encode(),
                BeforeDisplay = Display(before.Live), AfterDisplay = Display(value), Category = ChangeCategory.Modify,
                Enforcement = new SettingEnforcement { SkuRestriction = WindowsSku.Pro },
            }],
        };
    }

    private static string Normalize(string value) => value.Length == 0 ? "0" : value;
    private static bool Valid(LocalPolicyValue? value) => LocalPolicyToggleCatalog.Valid(value);

    public static bool Allows(ChangeDescriptor change) => change.ModuleId == "Windows Update" && change.SettingId == Id
        && change.SystemLocation.Equals(Location, StringComparison.OrdinalIgnoreCase)
        && change.ValueType == ChangeValueType.LocalPolicy_DWord
        && change.Enforcement is { SkuRestriction: WindowsSku.Pro, AclElevation: false, OwnerModeRequired: false }
        && change.Enforcement.CompanionServices is not { Count: > 0 } && change.Enforcement.CompanionTasks is not { Count: > 0 }
        && change.Enforcement.GPCacheEntries is not { Count: > 0 } && change.Enforcement.ReversionVectors is not { Count: > 0 }
        && Valid(LocalPolicyValue.Decode(change.BeforeValue)) && Valid(LocalPolicyValue.Decode(change.AfterValue));
}
