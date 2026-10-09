using System.Globalization;
using ThisIsMyPC.Core.Cards;
using ThisIsMyPC.Core.Changes;
using ThisIsMyPC.Core.Modules;
using ThisIsMyPC.Core.Policies;
using ThisIsMyPC.Core.Results;
using ThisIsMyPC.Core.Services;
using ThisIsMyPC.Modules.WindowsUpdate.Changes;

namespace ThisIsMyPC.Modules.WindowsUpdate.Services;

/// <summary>Mode choices and their dependent values, captured as one reversible group.</summary>
public sealed class WindowsUpdateChoiceSettings(IRegistryService registry,
    ICapabilityDetector? capabilities = null, PolicyControlStateReader? policies = null)
{
    public static bool Supports(string id) => id is "auto-update-mode" or "delivery-optimization" or "active-hours-manual";
    public static string Name(string id) => id switch
    {
        "auto-update-mode" => "Update installation",
        "delivery-optimization" => "Update sharing",
        "active-hours-manual" => "Active hours",
        _ => throw new ArgumentException("Unknown update setting.", nameof(id)),
    };
    public static IReadOnlyList<SettingOption> Options(string id) => id switch
    {
        "auto-update-mode" => [new("", "Windows default"), new("2", "Notify before download"),
            new("3", "Download automatically"), new("4", "Scheduled installation")],
        "delivery-optimization" => [new("", "Windows default"), new("0", "No peer sharing"),
            new("1", "Local network"), new("3", "Local network and internet"), new("2", "Private group")],
        "active-hours-manual" => [new("1", "Automatic"), new("2", "Manual")],
        _ => [],
    };
    private static string Key(string id) => id switch
    {
        "auto-update-mode" => WindowsUpdateRegistryPaths.AuPoliciesKeyPath,
        "delivery-optimization" => WindowsUpdateRegistryPaths.DeliveryOptimizationPoliciesKeyPath,
        _ => WindowsUpdateRegistryPaths.UxSettingsKeyPath,
    };
    private static string ValueName(string id) => id switch
    {
        "auto-update-mode" => "AUOptions", "delivery-optimization" => "DODownloadMode", _ => "SmartActiveHoursState",
    };
    private static string Display(string id, string value) => Options(id).FirstOrDefault(o => o.Value == value)?.DisplayName
        ?? (value == "" ? "Windows default" : $"Other value ({value})");

    private string Read(string key, string name)
    {
        var result = registry.ReadValue(key, name);
        if (!result.IsSuccess && result.ErrorCategory == ErrorCategory.NotFound) return "";
        if (!result.IsSuccess || result.Value!.Kind != RegistryValueDataKind.DWord)
            throw new InvalidOperationException($"Could not read {name} as a number.");
        return result.Value.Data;
    }
    public string ReadCurrent(string id)
    {
        var value = Read(Key(id), ValueName(id));
        return id == "active-hours-manual" && value == "" ? "1" : value;
    }
    private string ReadField(string id, string name, string fallback)
    {
        var value = Read(Key(id), name);
        return value == "" ? fallback : value;
    }
    private static readonly SettingOption[] Hours = Enumerable.Range(0, 24)
        .Select(hour => new SettingOption(hour.ToString(CultureInfo.InvariantCulture), new TimeOnly(hour, 0).ToString("t", CultureInfo.CurrentCulture))).ToArray();
    private static readonly SettingOption[] Days = [new("0", "Every day"), .. Enumerable.Range(0, 7)
        .Select(day => new SettingOption((day + 1).ToString(CultureInfo.InvariantCulture), CultureInfo.CurrentCulture.DateTimeFormat.GetDayName((DayOfWeek)day)))];
    public IReadOnlyList<SettingChoiceField> Fields(string id) => id switch
    {
        "auto-update-mode" => [new("ScheduledInstallDay", "Install day", "4", Days, () => ReadField(id, "ScheduledInstallDay", "0")),
            new("ScheduledInstallTime", "Install time", "4", Hours, () => ReadField(id, "ScheduledInstallTime", "3"))],
        "active-hours-manual" => [new("ActiveHoursStart", "Start time", "2", Hours, () => ReadField(id, "ActiveHoursStart", "8")),
            new("ActiveHoursEnd", "End time", "2", Hours, () => ReadField(id, "ActiveHoursEnd", "17"))],
        _ => [],
    };
    private PolicyControlState State(string id)
    {
        try
        {
            ValidateValues(id);
            var reader = policies ?? new PolicyControlStateReader(registry);
            var state = reader.Read("Windows Update", id, Key(id) + "\\" + ValueName(id), ChangeValueType.Registry_DWord);
            foreach (var field in Fields(id))
            {
                // Check saved policy for each dependent value before allowing a composite edit.
                var other = reader.Read("Windows Update", id, Key(id) + "\\" + field.Id, ChangeValueType.Registry_DWord);
                if (other.BlocksChanges) return other;
            }
            return state;
        }
        catch (InvalidOperationException ex) { return new(ex.Message, true); }
    }
    private void ValidateValues(string id)
    {
        var value = ReadCurrent(id);
        if (!Options(id).Any(option => option.Value == value))
            throw new InvalidOperationException("This Windows setting contains an unsupported value. Review it in Windows Settings.");
        foreach (var field in Fields(id))
        {
            var fieldValue = field.ReadCurrentValue();
            if (!field.Options.Any(option => option.Value == fieldValue))
                throw new InvalidOperationException("A saved time or day is not supported by this control.");
        }
    }
    public SettingCardSource CreateCard(string id)
    {
        var state = State(id);
        string current;
        try { current = ReadCurrent(id); }
        catch (InvalidOperationException) { current = "unknown"; }
        return new()
        {
            Model = new()
            {
                ModuleId = "Windows Update", SettingId = id, DisplayName = Name(id),
                Description = id switch
                {
                    "auto-update-mode" => "Choose when updates download and install. Automatic downloads notify before installation. Windows default removes this policy.",
                    "delivery-optimization" => "Choose which PCs can share updates. Private group uses the configured group, domain, or site. Windows default removes this policy.",
                    _ => "Choose active hours automatically or set a manual range of up to 18 hours.",
                },
                ControlType = SettingControlType.Dropdown, CurrentValue = current, CurrentDisplayValue = Display(id, current),
                AvailableOptions = Options(id), RegistryPath = Key(id), ValueName = ValueName(id),
                RegistryValueType = nameof(ChangeValueType.Registry_DWord),
                GroupId = id == "auto-update-mode" ? "Update Behavior" : id == "delivery-optimization" ? "Delivery Optimization" : "Update Experience",
                SkuRestriction = id == "active-hours-manual" ? null : WindowsSku.Pro,
                Enforcement = id == "auto-update-mode" ? new EnforcementProfile { Level = EnforcementLevel.Enforced,
                    Summary = "Applied with the Update Orchestrator's policy cache cleared" } : null,
            },
            ChoiceFields = state.BlocksChanges ? [] : Fields(id),
            ReadCurrentState = () => false, CreateToggleGroup = _ => throw new InvalidOperationException("Choose an option."),
            ReadCurrentValue = () => ReadCurrent(id), ReadPolicyState = () => State(id),
            CreateChoiceGroup = value => Create(id, value),
            CreateConfiguredChoiceGroup = (value, fields) => Create(id, value, fields),
        };
    }
    public ChangeGroup Create(string id, string value, IReadOnlyDictionary<string, string>? values = null)
        => Build(id, value, values, checkPolicy: true);

    // Preset preview needs descriptors to report edition and policy blockers. The queue checks again before staging.
    internal ChangeGroup CreateForPreset(string id, string value, IReadOnlyDictionary<string, string> values)
        => Build(id, value, values, checkPolicy: false);

    private ChangeGroup Build(string id, string value, IReadOnlyDictionary<string, string>? values, bool checkPolicy)
    {
        if (!Options(id).Any(option => option.Value == value)) throw new ArgumentException("Unsupported update option.", nameof(value));
        ValidateValues(id);
        var state = State(id);
        if (checkPolicy && state.BlocksChanges) throw new InvalidOperationException(state.Message);
        if (checkPolicy && capabilities is not null && id != "active-hours-manual" && SettingEditionSupport.BlockReason(capabilities.Sku, WindowsSku.Pro) is { } reason)
            throw new InvalidOperationException(reason);
        var fields = Fields(id).Where(field => field.ModeValue == value).ToArray();
        var desired = fields.ToDictionary(field => field.Id, field => values?.GetValueOrDefault(field.Id) ?? field.ReadCurrentValue());
        foreach (var field in fields)
            if (!field.Options.Any(option => option.Value == desired[field.Id])) throw new ArgumentException("Choose a valid " + field.DisplayName.ToLowerInvariant() + ".");
        if (id == "active-hours-manual" && value == "2")
        {
            var length = (int.Parse(desired["ActiveHoursEnd"], CultureInfo.InvariantCulture) - int.Parse(desired["ActiveHoursStart"], CultureInfo.InvariantCulture) + 24) % 24;
            var limitValue = Read(WindowsUpdateRegistryPaths.WindowsUpdatePoliciesKeyPath, "ActiveHoursMaxRange");
            var limit = limitValue == "" ? 18 : int.Parse(limitValue, CultureInfo.InvariantCulture);
            if (limit is < 8 or > 18) throw new InvalidOperationException("The active hours limit is not recognized.");
            if (length == 0 || length > limit) throw new ArgumentException($"Active hours must span between 1 and {limit} hours.");
        }
        var changes = new List<ChangeDescriptor> { Change(id, ValueName(id), value, Name(id), Display(id, ReadCurrent(id)), Display(id, value)) };
        changes.AddRange(fields.Select(field => Change(id, field.Id, desired[field.Id], field.DisplayName,
            field.Options.FirstOrDefault(option => option.Value == field.ReadCurrentValue())?.DisplayName ?? "Unknown",
            field.Options.Single(option => option.Value == desired[field.Id]).DisplayName)));
        return new() { GroupId = Guid.NewGuid().ToString("N"), DisplayName = Name(id), Description = Name(id), Changes = changes };
    }
    private ChangeDescriptor Change(string id, string name, string value, string label, string beforeDisplay, string afterDisplay) => new()
    {
        ModuleId = "Windows Update", SettingId = id, DisplayName = label, SystemLocation = Key(id) + "\\" + name,
        BeforeValue = Read(Key(id), name), AfterValue = value, BeforeDisplay = beforeDisplay, AfterDisplay = afterDisplay,
        ValueType = ChangeValueType.Registry_DWord, Category = ChangeCategory.Modify,
        Enforcement = id == "auto-update-mode" ? WindowsUpdateChangeFactory.WUPolicyEnforcement
            : id == "delivery-optimization" ? WindowsUpdateChangeFactory.DOPolicyEnforcement : null,
    };
}
