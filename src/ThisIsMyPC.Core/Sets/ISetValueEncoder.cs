using ThisIsMyPC.Core.Changes;

namespace ThisIsMyPC.Core.Sets;

/// <summary>Encodes a complete policy choice for preset export, including companion values.</summary>
public interface ISetValueEncoder
{
    string ModuleId { get; }
    string? Encode(string settingId, IReadOnlyList<SetValue> values);
}

public sealed record SetValue(string Location, ChangeValueType Type, string? Value);
