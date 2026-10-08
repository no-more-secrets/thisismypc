using System.Collections.Immutable;
using System.Text;
using ThisIsMyPC.Core.Changes;

namespace ThisIsMyPC.Core.Policies;

/// <summary>Verifies the complete saved document before a loaded GPO hive can be reused for rollback.</summary>
public sealed class LocalPolicySaveGuard(byte[]? original)
{
    public byte[]? Expected { get; private set; } = original;
    private ImmutableArray<RegistryPolicyEntry> _staged;

    public void Stage(string location, ChangeValueType type, LocalPolicyValue value)
    {
        var split = location.LastIndexOf('\\');
        var key = location[5..split];
        var name = location[(split + 1)..];
        var entries = Expected is null ? [] : RegistryPolicyFile.Parse(Expected);
        _staged = entries.Where(e => !e.KeyPath.Equals(key, StringComparison.OrdinalIgnoreCase)
            || !e.ValueName.Equals(name, StringComparison.OrdinalIgnoreCase)
                && !e.ValueName.Equals("**del." + name, StringComparison.OrdinalIgnoreCase)).ToImmutableArray();
        if (value.Delete) _staged = _staged.Add(new(key, "**del." + name, 1, [32, 0, 0, 0]));
        else if (value.Saved is not null)
            _staged = _staged.Add(new(key, name, type == ChangeValueType.LocalPolicy_String ? 1u : 4u,
                (type == ChangeValueType.LocalPolicy_String ? Encoding.Unicode.GetBytes(value.Saved + '\0')
                    : BitConverter.GetBytes(int.Parse(value.Saved, System.Globalization.CultureInfo.InvariantCulture))).ToImmutableArray()));
    }

    public void AcceptSaved(byte[]? actual)
    {
        var entries = actual is null ? [] : RegistryPolicyFile.Parse(actual);
        if (_staged.IsDefault || !Canonical(entries).SequenceEqual(Canonical(_staged), StringComparer.Ordinal))
            throw new InvalidOperationException("The saved policy document differs from the requested change. Refresh before any further edits.");
        Expected = actual;
    }

    private static IEnumerable<string> Canonical(IEnumerable<RegistryPolicyEntry> entries) => entries
        // GPO may add/remove empty key records when opening or saving a hive. They carry no value instruction.
        .Where(e => !(e.ValueName.Length == 0 && e.ValueType == 0 && e.Data.Length == 0))
        .Select(e => e.KeyPath.ToUpperInvariant() + '\0' + e.ValueName.ToUpperInvariant() + '\0'
            + e.ValueType.ToString(System.Globalization.CultureInfo.InvariantCulture) + '\0' + Convert.ToBase64String(e.Data.AsSpan()))
        .Order(StringComparer.Ordinal);
}
