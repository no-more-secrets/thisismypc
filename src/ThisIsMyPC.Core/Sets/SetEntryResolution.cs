namespace ThisIsMyPC.Core.Sets;

/// <summary>How a set entry relates to the current system and pending changes.</summary>
public enum SetEntryConflict
{
    /// <summary>Stageable, nothing in the way.</summary>
    None,

    /// <summary>The system already matches the desired value; excluded by default.</summary>
    AlreadyApplied,

    /// <summary>A pending change already targets this setting with the same value.</summary>
    PendingSameValue,

    /// <summary>
    /// A pending change targets this setting with a DIFFERENT value. Excluded by default
    /// (keep the pending change); including the entry replaces it at stage time.
    /// </summary>
    PendingDifferentValue,
}

/// <summary>One set entry fully resolved for preview and staging decisions.</summary>
public sealed record SetEntryResolution
{
    public required SetEntry Entry { get; init; }

    /// <summary>Live state; null when the entry is skipped.</summary>
    public required SetEntryState? State { get; init; }

    /// <summary>Non-null marks the entry unstageable, with the user-facing reason.</summary>
    public required string? SkipReason { get; init; }

    public required SetEntryConflict Conflict { get; init; }

    /// <summary>GroupId of the conflicting pending group (PendingSame/DifferentValue).</summary>
    public string? PendingGroupId { get; init; }

    /// <summary>The conflicting pending change's AfterValue.</summary>
    public string? PendingValue { get; init; }

    /// <summary>The conflicting pending change's human-readable AfterDisplay.</summary>
    public string? PendingDisplay { get; init; }

    /// <summary>
    /// Informational notice when the entry is cosmetic on the current Windows edition
    /// (SettingEnforcement.SkuRestriction matches the detected SKU). Never blocks
    /// staging or changes the default inclusion.
    /// </summary>
    public string? SkuNotice { get; init; }

    public bool IsSkipped => SkipReason is not null;

    /// <summary>Checked by default only when stageable with nothing in the way.</summary>
    public bool IncludedByDefault => !IsSkipped && Conflict == SetEntryConflict.None;
}
