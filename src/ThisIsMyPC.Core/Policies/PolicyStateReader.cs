using System.Buffers.Binary;
using System.Collections.Immutable;
using System.Text;
using ThisIsMyPC.Core.Results;
using ThisIsMyPC.Core.Services;

namespace ThisIsMyPC.Core.Policies;

/// <summary>Reads policy evidence without treating absent, unreadable, or wrong-type values as enabled features.</summary>
public static class PolicyStateReader
{
    /// <summary>Sources must be supplied in local application order, lowest precedence first.</summary>
    public static PolicyObservation Read(PolicyDefinition definition, PolicyScope scope,
        IEnumerable<PolicySourceSnapshot> sources, OperationResult<RegistryValueData> value)
    {
        ArgumentNullException.ThrowIfNull(definition);
        ArgumentNullException.ThrowIfNull(sources);
        ArgumentNullException.ThrowIfNull(value);
        if (!definition.Scopes.Contains(scope)) throw new ArgumentException("Scope is not supported by this policy.", nameof(scope));
        var sourceStates = ImmutableArray.CreateBuilder<PolicySourceState>();
        var saved = PolicyState.NotConfigured;
        foreach (var source in sources.Where(s => s.Scope == scope))
        {
            var state = ReadSource(definition, source);
            sourceStates.Add(new(source.Name, state, source.Error));
            if (state != PolicyState.NotConfigured) saved = state;
        }
        // No evidence was supplied for this scope. This does not establish an empty policy store.
        if (sourceStates.Count == 0) saved = PolicyState.Unknown;
        var actual = value switch
        {
            { IsSuccess: false, ErrorCategory: ErrorCategory.NotFound } => PolicyState.NotConfigured,
            { IsSuccess: false } => PolicyState.Unknown,
            { Value.Kind: RegistryValueDataKind.DWord } => DecodeRegistryValue(definition, value.Value),
            _ => PolicyState.OtherValue,
        };
        return new(definition, scope, sourceStates.ToImmutable(), saved, actual,
            actual == PolicyState.Unknown ? value.ErrorMessage : null);
    }

    private static PolicyState DecodeRegistryValue(PolicyDefinition definition, RegistryValueData value) =>
        int.TryParse(value.Data, System.Globalization.NumberStyles.Integer, System.Globalization.CultureInfo.InvariantCulture, out var number)
            ? definition.Decode(unchecked((uint)number)) : PolicyState.OtherValue;

    public static PolicyState ReadSource(PolicyDefinition definition, PolicySourceSnapshot source)
    {
        ArgumentNullException.ThrowIfNull(definition);
        ArgumentNullException.ThrowIfNull(source);
        if (!definition.Scopes.Contains(source.Scope)) throw new ArgumentException("Scope is not supported by this policy.", nameof(source));
        if (source.Status == PolicyFileStatus.Unreadable) return PolicyState.Unknown;
        if (source.Status == PolicyFileStatus.Missing) return PolicyState.NotConfigured;
        if (source.Entries.IsDefault) return PolicyState.Unknown;
        var state = PolicyState.NotConfigured;
        foreach (var entry in source.Entries)
        {
            var sameKey = Equal(entry.KeyPath, definition.KeyPath);
            if (!entry.IsDirective)
            {
                if (sameKey && Equal(entry.ValueName, definition.ValueName))
                    state = entry.ValueType == 4 && entry.Data.Length == 4
                        ? definition.Decode(BinaryPrimitives.ReadUInt32LittleEndian(entry.Data.AsSpan())) : PolicyState.OtherValue;
                continue;
            }
            if (!sameKey && !definition.KeyPath.StartsWith(entry.KeyPath + "\\", StringComparison.OrdinalIgnoreCase)) continue;
            if (Equal(entry.ValueName, "**SecureKey")) continue;
            if (Equal(entry.ValueName, "**DeleteKeys"))
            {
                var keys = ReadList(entry);
                if (keys is null || keys.Any(key => key.Contains('\\', StringComparison.Ordinal))) { state = PolicyState.Unknown; continue; }
                if (keys.Any(key => Equal(definition.KeyPath, entry.KeyPath + "\\" + key) ||
                    definition.KeyPath.StartsWith(entry.KeyPath + "\\" + key + "\\", StringComparison.OrdinalIgnoreCase)))
                    state = PolicyState.DeleteValue;
                continue;
            }
            if (!sameKey) continue;
            if (entry.ValueName.StartsWith("**Del.", StringComparison.OrdinalIgnoreCase))
            {
                if (Equal(entry.ValueName[6..], definition.ValueName))
                    state = IsDeletePayload(entry) ? PolicyState.DeleteValue : PolicyState.Unknown;
            }
            else if (Equal(entry.ValueName, "**DelVals."))
                state = IsDeletePayload(entry) ? PolicyState.DeleteValue : PolicyState.Unknown;
            else if (Equal(entry.ValueName, "**DeleteValues"))
            {
                var values = ReadList(entry);
                if (values is null) state = PolicyState.Unknown;
                else if (values.Any(name => Equal(name, definition.ValueName))) state = PolicyState.DeleteValue;
            }
            else state = PolicyState.Unknown;
        }
        return state;
    }

    private static bool IsDeletePayload(RegistryPolicyEntry entry) =>
        entry.ValueType == 1 && entry.Data.AsSpan().SequenceEqual(new byte[] { 32, 0, 0, 0 });

    private static string[]? ReadList(RegistryPolicyEntry entry)
    {
        if (entry.ValueType != 1 || entry.Data.Length < 2 || entry.Data.Length % 2 != 0 ||
            entry.Data[^1] != 0 || entry.Data[^2] != 0) return null;
        try
        {
            var text = new UnicodeEncoding(false, false, true).GetString(entry.Data.AsSpan()[..^2]);
            return text.Contains('\0', StringComparison.Ordinal) ? null : text.Split(';', StringSplitOptions.RemoveEmptyEntries);
        }
        catch (DecoderFallbackException) { return null; }
    }

    private static bool Equal(string a, string b) => string.Equals(a, b, StringComparison.OrdinalIgnoreCase);
}
