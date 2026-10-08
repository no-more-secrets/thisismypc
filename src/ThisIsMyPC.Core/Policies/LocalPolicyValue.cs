using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using ThisIsMyPC.Core.Changes;
using ThisIsMyPC.Core.Results;
using ThisIsMyPC.Core.Services;

namespace ThisIsMyPC.Core.Policies;

/// <summary>One saved local policy instruction and its independently captured live value.</summary>
public sealed record LocalPolicyValue(string? Saved, string Live, bool Delete = false)
{
    public string Encode() => JsonSerializer.Serialize(this, LocalPolicyJson.Default.LocalPolicyValue);

    public static LocalPolicyValue? Decode(string? text)
    {
        if (text is null || text.Length > 4096) return null;
        try
        {
            var value = JsonSerializer.Deserialize(text, LocalPolicyJson.Default.LocalPolicyValue);
            return value is { Live: not null } && !(value.Delete && value.Saved is not null) ? value : null;
        }
        catch (JsonException) { return null; }
    }

    public static bool IsPolicyType(ChangeValueType type) => type is ChangeValueType.LocalPolicy_DWord or ChangeValueType.LocalPolicy_String;
    public static ChangeValueType RegistryType(ChangeValueType type) => type == ChangeValueType.LocalPolicy_String
        ? ChangeValueType.Registry_String : ChangeValueType.Registry_DWord;

    /// <summary>Rejects ambiguous directives and duplicates instead of rewriting unrelated instructions.</summary>
    public static LocalPolicyValue Read(string location, ChangeValueType type, IReadOnlyList<PolicySourceSnapshot> sources,
        OperationResult<RegistryValueData> live)
    {
        var machines = sources.Where(s => s.Scope == PolicyScope.Machine).ToArray();
        if (machines.Length != 1 || machines[0].Status == PolicyFileStatus.Unreadable)
            throw new InvalidOperationException("Saved local computer policy could not be read.");
        if (!location.StartsWith("HKLM\\", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Only local computer policies are supported.");
        var split = location.LastIndexOf('\\');
        var key = location[5..split];
        var name = location[(split + 1)..];
        var kind = RegistryType(type) == ChangeValueType.Registry_String ? RegistryValueDataKind.String : RegistryValueDataKind.DWord;
        var current = live.IsSuccess ? live.Value!.Kind == kind ? live.Value.Data
            : throw new InvalidOperationException("The current policy value has an unexpected type.")
            : live.ErrorCategory == ErrorCategory.NotFound ? "" : throw new InvalidOperationException("The current policy value could not be read.");
        string? saved = null;
        var delete = false;
        var count = 0;
        var source = machines[0];
        if (source.Status == PolicyFileStatus.Loaded)
        {
            if (source.Entries.IsDefault) throw new InvalidOperationException("Saved local computer policy is incomplete.");
            foreach (var entry in source.Entries)
            {
                var sameKey = entry.KeyPath.Equals(key, StringComparison.OrdinalIgnoreCase);
                if (!sameKey && !key.StartsWith(entry.KeyPath + "\\", StringComparison.OrdinalIgnoreCase)) continue;
                if (entry.IsDirective)
                {
                    if (entry.ValueName.Equals("**SecureKey", StringComparison.OrdinalIgnoreCase)) continue;
                    if (entry.ValueName.StartsWith("**Del.", StringComparison.OrdinalIgnoreCase))
                    {
                        if (!sameKey || !entry.ValueName[6..].Equals(name, StringComparison.OrdinalIgnoreCase)) continue;
                        if (entry.ValueType != 1 || !entry.Data.AsSpan().SequenceEqual(new byte[] { 32, 0, 0, 0 }))
                            throw new InvalidOperationException("The saved deletion instruction is not recognized.");
                        delete = true;
                        count++;
                    }
                    else throw new InvalidOperationException("A broader saved policy instruction controls this setting. Edit it in Group Policy Editor.");
                }
                else if (sameKey && entry.ValueName.Equals(name, StringComparison.OrdinalIgnoreCase))
                {
                    count++;
                    if (kind == RegistryValueDataKind.DWord && entry.ValueType == 4 && entry.Data.Length == 4)
                        saved = BitConverter.ToInt32(entry.Data.AsSpan()).ToString(System.Globalization.CultureInfo.InvariantCulture);
                    else if (kind == RegistryValueDataKind.String && entry.ValueType == 1 && entry.Data.Length >= 2
                        && entry.Data.Length % 2 == 0 && entry.Data[^1] == 0 && entry.Data[^2] == 0)
                    {
                        saved = new UnicodeEncoding(false, false, true).GetString(entry.Data.AsSpan()[..^2]);
                        if (saved.Contains('\0')) throw new InvalidOperationException("The saved policy string is malformed.");
                    }
                    else throw new InvalidOperationException("The saved policy value has an unexpected type.");
                }
            }
        }
        if (count > 1) throw new InvalidOperationException("Duplicate saved policy instructions require review in Group Policy Editor.");
        return new(saved, current, delete);
    }
}

[JsonSerializable(typeof(LocalPolicyValue))]
internal partial class LocalPolicyJson : JsonSerializerContext;

/// <summary>Edits a validated local computer policy and its live value with rollback.</summary>
public interface ILocalPolicyService
{
    OperationResult<bool> Apply(string location, ChangeValueType type, LocalPolicyValue before, LocalPolicyValue after);
}
