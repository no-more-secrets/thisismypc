using ThisIsMyPC.Core.Results;

namespace ThisIsMyPC.Core.Actions;

public sealed record ActionFailure(
    ActionDescriptor Action,
    string? ErrorMessage,
    ErrorCategory? ErrorCategory);

/// <summary>
/// Outcome of one action batch. Actions are independent, so execution continues
/// past failures; <see cref="Failed"/> can be non-empty alongside <see cref="Succeeded"/>.
/// </summary>
public sealed record ActionBatchResult
{
    public required IReadOnlyList<ActionDescriptor> Succeeded { get; init; }
    public required IReadOnlyList<ActionFailure> Failed { get; init; }

    public bool IsSuccess => Failed.Count == 0;
}
