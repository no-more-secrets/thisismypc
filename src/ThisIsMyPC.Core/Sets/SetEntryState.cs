namespace ThisIsMyPC.Core.Sets;

/// <summary>Live system state of one set entry, as resolved by an ISetEntryInspector.</summary>
public sealed record SetEntryState
{
    /// <summary>The module's display name for the setting, e.g. "Taskbar alignment".</summary>
    public required string SettingDisplayName { get; init; }

    /// <summary>
    /// Raw current value in the module's value-string convention. For group toggles this
    /// is the group's primary value (the same one SetEntry.Value uses).
    /// </summary>
    public required string CurrentValue { get; init; }

    /// <summary>Human rendering of the current state, e.g. "Suppressed", "Partially set".</summary>
    public required string CurrentDisplay { get; init; }

    /// <summary>
    /// True when the system already matches the entry's desired value. For group toggles
    /// every constituent value must match; partial states are not applied.
    /// </summary>
    public required bool IsApplied { get; init; }
}
