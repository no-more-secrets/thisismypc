using System.Collections.Immutable;

namespace ThisIsMyPC.Core.Policies;

public enum PolicyScope { Machine, User }
public enum PolicyState { NotConfigured, Enabled, Disabled, DeleteValue, OtherValue, Unknown }
public enum PolicyFileStatus { Loaded, Missing, Unreadable }
public enum PolicyComparison { Matches, Differs, RegistryOnly, NoSetting, Unknown }

/// <summary>A verified, single-DWORD ADMX mapping. These are policy states, not feature on/off states.</summary>
public sealed record PolicyDefinition(
    string Id, string DisplayName, string ModuleId, string Section, string KeyPath, string ValueName,
    ImmutableArray<PolicyScope> Scopes, uint EnabledValue, uint DisabledValue, string AdmxFile, string AdmxPolicyId,
    string DocumentationUrl)
{
    public PolicyState Decode(uint value) => value == EnabledValue ? PolicyState.Enabled :
        value == DisabledValue ? PolicyState.Disabled : PolicyState.OtherValue;
}

/// <summary>One local policy source. Missing and unreadable files have different meanings.</summary>
public sealed record PolicySourceSnapshot(string Name, PolicyScope Scope, PolicyFileStatus Status,
    ImmutableArray<RegistryPolicyEntry> Entries, string? Error = null);

public sealed record PolicySourceState(string Name, PolicyState State, string? Error);

/// <summary>Saved local configuration compared with the registry. Neither proves that Windows honors the setting.</summary>
public sealed record PolicyObservation(PolicyDefinition Definition, PolicyScope Scope,
    ImmutableArray<PolicySourceState> Sources, PolicyState SavedState, PolicyState RegistryState, string? RegistryError)
{
    public PolicyComparison Comparison => (SavedState, RegistryState) switch
    {
        (PolicyState.Unknown or PolicyState.OtherValue, _) or (_, PolicyState.Unknown or PolicyState.OtherValue) => PolicyComparison.Unknown,
        (PolicyState.NotConfigured, PolicyState.NotConfigured) => PolicyComparison.NoSetting,
        (PolicyState.NotConfigured, _) => PolicyComparison.RegistryOnly,
        (PolicyState.DeleteValue, PolicyState.NotConfigured) => PolicyComparison.Matches,
        var (saved, registry) when saved == registry => PolicyComparison.Matches,
        _ => PolicyComparison.Differs,
    };
}
