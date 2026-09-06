namespace ThisIsMyPC.Core.Changes;

public record ChangeHistoryEntry
{
    public string? JournalOutcome { get; init; }
    public string? JournalDetail { get; init; }
    public Guid? OwnerAttemptId { get; init; }
    public string? TargetUserSid { get; init; }
    public bool CanExecuteHistoryAction => SupportsGenericUndo;
    public bool CanCreateCustomSet => SupportsGenericUndo;
    public bool SupportsGenericUndo => OwnerAttemptId is null && TargetUserSid is null && Category != ChangeCategory.SystemReversion;
    public long Id { get; init; }
    public required string ModuleId { get; init; }
    public required string SettingId { get; init; }
    public required string DisplayName { get; init; }
    public required string SystemLocation { get; init; }
    public string? BeforeValue { get; init; }
    public string? AfterValue { get; init; }
    public string? BeforeDisplay { get; init; }
    public string? AfterDisplay { get; init; }
    public required ChangeValueType ValueType { get; init; }
    public ChangeCategory Category { get; init; }
    public string? GroupId { get; init; }
    public required DateTimeOffset AppliedAt { get; init; }
    public DateTimeOffset? RevertedAt { get; init; }
    public long? RevertedByEntryId { get; init; }
    public long? RedoOfEntryId { get; init; }

    /// <summary>
    /// The change's enforcement metadata, persisted so history undo/redo can route
    /// through the enforcement executor (a bare module revert would leave companion
    /// services/tasks/GPCache untouched; e.g. the WU orchestrator's policy cache
    /// keeping an undone policy alive).
    /// </summary>
    public Enforcement.SettingEnforcement? Enforcement { get; init; }
}
