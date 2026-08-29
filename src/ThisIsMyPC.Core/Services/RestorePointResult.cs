namespace ThisIsMyPC.Core.Services;

public enum RestorePointOutcome
{
    Created,

    /// <summary>System Restore is disabled on this machine (policy or never enabled).</summary>
    SystemRestoreDisabled,

    Failed,
}

public sealed record RestorePointResult
{
    public required RestorePointOutcome Outcome { get; init; }
    public long? SequenceNumber { get; init; }
    public string? Message { get; init; }

    public bool IsSuccess => Outcome == RestorePointOutcome.Created;
}
