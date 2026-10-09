using ThisIsMyPC.Core.Changes;
using ThisIsMyPC.Core.Policies;
using ThisIsMyPC.Core.Sets;

namespace ThisIsMyPC.Modules.WindowsUpdate.Services;

/// <summary>Preserves scheduled times in presets instead of exporting only the mode.</summary>
public sealed class WindowsUpdateSetValueEncoder : ISetValueEncoder
{
    public string ModuleId => "Windows Update";
    public string? Encode(string settingId, IReadOnlyList<SetValue> values)
    {
        if (values.Count == 0) return null;
        string? Read(string name) => values.FirstOrDefault(value => value.Location.EndsWith("\\" + name, StringComparison.OrdinalIgnoreCase))?.Value;
        var mode = settingId switch
        {
            "auto-update-mode" => Read("AUOptions"), "active-hours-manual" => Read("SmartActiveHoursState"),
            _ => LocalPolicyValue.IsPolicyType(values[0].Type) ? LocalPolicyValue.Decode(values[0].Value)?.Live : values[0].Value,
        };
        var names = FieldNames(settingId, mode);
        if (names.Length == 0) return mode;
        var parts = new[] { mode, Read(names[0]), Read(names[1]) };
        return parts.Any(part => part is null) ? null : string.Join("|", parts);
    }
    private static string[] FieldNames(string id, string? mode) => (id, mode) switch
    {
        ("auto-update-mode", "4") => ["ScheduledInstallDay", "ScheduledInstallTime"],
        ("active-hours-manual", "2") => ["ActiveHoursStart", "ActiveHoursEnd"], _ => [],
    };
    public static bool TryDecode(string id, string value, out string mode, out IReadOnlyDictionary<string, string> fields)
    {
        var parts = value.Split('|');
        mode = parts[0];
        fields = new Dictionary<string, string>();
        if (!WindowsUpdateChoiceSettings.Options(id).Any(option => option.Value == parts[0])) return false;
        if (parts.Length == 1) return true; // Existing presets retain their original mode-only meaning.
        var names = FieldNames(id, mode);
        if (parts.Length != 3 || names.Length != 2) return false;
        if (!int.TryParse(parts[1], out var first) || !int.TryParse(parts[2], out var second)
            || first < 0 || first > (id == "auto-update-mode" ? 7 : 23) || second is < 0 or > 23) return false;
        if (id == "active-hours-manual" && (second - first + 24) % 24 is 0 or > 18) return false;
        fields = new Dictionary<string, string> { [names[0]] = parts[1], [names[1]] = parts[2] };
        return true;
    }
}
