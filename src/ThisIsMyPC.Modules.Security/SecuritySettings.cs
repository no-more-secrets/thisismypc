using ThisIsMyPC.Core.Changes;
using ThisIsMyPC.Core.Enforcement;
using ThisIsMyPC.Core.Policies;
using ThisIsMyPC.Core.Results;
using ThisIsMyPC.Core.Services;

namespace ThisIsMyPC.Modules.Security;

public sealed record SecuritySnapshot(SecuritySetting Setting, IReadOnlyList<string> Values, SecurityOption? Option,
    string? BlockReason)
{
    public IReadOnlyList<LocalPolicyValue>? SavedValues { get; init; }
    public string? PolicyNotice { get; init; }
    public string Display => Option?.Label ?? (BlockReason is not null ? "Unknown policy state" : "Custom policy combination");
}

public sealed class SecuritySettings(IRegistryService registry, PolicyControlStateReader? policies = null)
{
    public SecuritySnapshot Read(SecuritySetting setting)
    {
        var values = new List<string>();
        var reasons = new List<string>();
        var readable = true;
        var savedValues = new List<LocalPolicyValue>();
        var editSaved = policies?.HasLocalSourceReader == true && setting.Id != "secure-sign-in";
        IReadOnlyList<PolicySourceSnapshot>? sources = null;
        if (editSaved)
        {
            try { sources = policies!.ReadLocalSources(); }
            catch (Exception ex) when (ex is IOException or InvalidOperationException or UnauthorizedAccessException or System.Security.SecurityException)
            { reasons.Add("Saved local policy could not be read."); }
        }
        for (var index = 0; index < setting.Targets.Count; index++)
        {
            var target = setting.Targets[index];
            var read = registry.ReadValue(target.Key, target.Name);
            var value = "";
            if (!read.IsSuccess)
            {
                if (read.ErrorCategory != ErrorCategory.NotFound) { reasons.Add("The policy value could not be read."); readable = false; }
            }
            else if (read.Value!.Kind != (target.Type == ChangeValueType.Registry_String ? RegistryValueDataKind.String : RegistryValueDataKind.DWord))
            { reasons.Add("The policy value has an unexpected type."); readable = false; }
            else
            {
                value = read.Value.Data;
                if (value == "" || !setting.Choices.Any(o => o.Values[index] == value)) { reasons.Add("The policy value is not recognized."); readable = false; }
            }
            values.Add(value);
            if (editSaved)
            {
                try
                {
                    var saved = LocalPolicyValue.Read(target.Location, target.Type == ChangeValueType.Registry_String
                        ? ChangeValueType.LocalPolicy_String : ChangeValueType.LocalPolicy_DWord, sources ?? [], read);
                    if (saved.Saved is not null && saved.Saved != value || saved.Delete && value != "")
                        reasons.Add("Saved local policy differs from the current registry value. Refresh policy before editing.");
                    savedValues.Add(saved);
                }
                catch (Exception ex) when (ex is InvalidOperationException or System.Text.DecoderFallbackException)
                { reasons.Add(ex.Message); }
                continue;
            }
            var state = policies?.Read(SecurityCatalog.ModuleId, setting.Id, target.Location, target.Type);
            if (state?.BlocksChanges == true)
            {
                var message = state.Message ?? "The policy source could not be verified.";
                // The option already describes the value. Keep source evidence readable.
                message = message.Replace("Current policy value: " + value + ".", "", StringComparison.Ordinal).Trim();
                if (message == "Controlled by saved local policy.")
                    message = "Controlled by saved local policy. Editing saved policy is not supported yet.";
                reasons.Add(message);
            }
        }
        var option = readable ? setting.Choices.FirstOrDefault(o => o.Values.SequenceEqual(values)) : null;
        return new(setting, values, option, reasons.Count == 0 ? null : string.Join(" ", reasons.Distinct()))
        {
            SavedValues = editSaved && savedValues.Count == setting.Targets.Count ? savedValues : null,
            PolicyNotice = savedValues.Any(v => v.Saved is not null || v.Delete)
                ? "Apply updates saved local policy and its current value. Undo restores both." : null,
        };
    }

    public ChangeGroup Create(SecuritySetting setting, SecurityOption option)
    {
        if (!setting.Choices.Contains(option)) throw new ArgumentException("Unknown security option.", nameof(option));
        var before = Read(setting);
        if (before.BlockReason is not null) throw new InvalidOperationException(before.BlockReason);
        return new ChangeGroup
        {
            GroupId = Guid.NewGuid().ToString("N"), DisplayName = setting.Title, Description = setting.Description,
            Changes = setting.Targets.Select((target, index) => new ChangeDescriptor
            {
                ModuleId = SecurityCatalog.ModuleId, SettingId = setting.Id, DisplayName = setting.Title,
                SystemLocation = target.Location,
                ValueType = before.SavedValues is null ? target.Type : target.Type == ChangeValueType.Registry_String
                    ? ChangeValueType.LocalPolicy_String : ChangeValueType.LocalPolicy_DWord,
                BeforeValue = before.SavedValues is null ? before.Values[index] : before.SavedValues[index].Encode(),
                AfterValue = before.SavedValues is null ? option.Values[index]
                    : new LocalPolicyValue(option.Values[index] == "" ? null : option.Values[index], option.Values[index]).Encode(),
                BeforeDisplay = before.Display, AfterDisplay = option.Label, Category = ChangeCategory.Modify,
                Enforcement = new SettingEnforcement { SkuRestriction = setting.Edition },
            }).ToList(),
        };
    }
}
