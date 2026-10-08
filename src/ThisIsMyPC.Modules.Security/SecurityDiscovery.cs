using ThisIsMyPC.Core.Changes;
using ThisIsMyPC.Core.Search;
using ThisIsMyPC.Core.Services;
using ThisIsMyPC.Core.Sets;
using ThisIsMyPC.Core.Policies;

namespace ThisIsMyPC.Modules.Security;

public sealed class SecuritySearchContributor : ISearchSettingsContributor
{
    public string ModuleId => SecurityCatalog.ModuleId;
    public IReadOnlyList<SearchEntry> GetSearchEntries() => SecurityCatalog.Settings.Select(s =>
        new SearchEntry(ModuleId, s.Id, s.Title, s.Description, s.Targets.Select(t => t.Name).Append(s.Section).ToList())).ToList();
}

public sealed class SecuritySetEntryInspector(IRegistryService registry, PolicyControlStateReader? policies = null) : ISetEntryInspector
{
    public string ModuleId => SecurityCatalog.ModuleId;
    public SetEntryState? Inspect(SetEntry entry)
    {
        var setting = SecurityCatalog.Settings.FirstOrDefault(s => s.Id == entry.SettingId);
        if (setting is null) return null;
        var state = new SecuritySettings(registry, policies).Read(setting);
        return new() { SettingDisplayName = setting.Title, CurrentValue = state.Option?.Id ?? "custom",
            CurrentDisplay = state.Display, IsApplied = state.BlockReason is null && state.Option?.Id == entry.Value };
    }
    public ChangeGroup? CreateChangeGroup(SetEntry entry)
    {
        var setting = SecurityCatalog.Settings.FirstOrDefault(s => s.Id == entry.SettingId);
        var option = setting?.Choices.FirstOrDefault(o => o.Id == entry.Value);
        if (setting is null || option is null) return null;
        var reader = new SecuritySettings(registry, policies);
        return reader.Read(setting).BlockReason is null ? reader.Create(setting, option) : null;
    }
}

public sealed class SecuritySetValueEncoder : ISetValueEncoder
{
    public string ModuleId => SecurityCatalog.ModuleId;
    public string? Encode(string settingId, IReadOnlyList<SetValue> values)
    {
        var setting = SecurityCatalog.Settings.FirstOrDefault(s => s.Id == settingId);
        if (setting is null || values.Count != setting.Targets.Count) return null;
        return setting.Choices.FirstOrDefault(option => setting.Targets.Select((target, index) =>
            values.Count(value => value.Location.Equals(target.Location, StringComparison.OrdinalIgnoreCase)
                && (LocalPolicyValue.IsPolicyType(value.Type) ? LocalPolicyValue.RegistryType(value.Type) == target.Type
                    && LocalPolicyValue.Decode(value.Value)?.Live == option.Values[index]
                    : value.Type == target.Type && value.Value == option.Values[index])) == 1).All(matches => matches))?.Id;
    }
}
