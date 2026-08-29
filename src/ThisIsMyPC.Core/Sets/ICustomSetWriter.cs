using ThisIsMyPC.Core.Changes;

namespace ThisIsMyPC.Core.Sets;

/// <summary>
/// Writes user-created set definitions (Story 8.5) as JSON files into the user sets
/// directory, where <see cref="ISetProvider"/> picks them up on the next load.
/// </summary>
public interface ICustomSetWriter
{
    /// <summary>Creates a set from staged pending changes, one entry per group.</summary>
    CustomSetWriteResult WriteFromPendingGroups(CustomSetMetadata metadata, IReadOnlyList<ChangeGroup> groups);

    /// <summary>
    /// Creates a set from change-history rows, one entry per applied batch
    /// (rows sharing a GroupId collapse into a single entry).
    /// </summary>
    CustomSetWriteResult WriteFromHistory(CustomSetMetadata metadata, IReadOnlyList<ChangeHistoryEntry> entries);
}

/// <summary>User-supplied metadata captured by the save-as-set form.</summary>
public sealed record CustomSetMetadata
{
    public required string Name { get; init; }
    public required string Description { get; init; }
    public required SetCategory Category { get; init; }
}

public sealed record CustomSetWriteResult
{
    /// <summary>Absolute path of the created file; null when <see cref="Error"/> is set.</summary>
    public string? FilePath { get; init; }

    /// <summary>Number of set entries written to the file.</summary>
    public int EntryCount { get; init; }

    /// <summary>Groups that could not be represented (no after-value to re-apply).</summary>
    public int SkippedGroupCount { get; init; }

    public string? Error { get; init; }

    public bool Success => Error is null;
}
