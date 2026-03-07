using ThisIsMyPC.Core.Results;

namespace ThisIsMyPC.Core.Changes;

public record MutationResult
{
    public bool IsSuccess { get; init; }
    public required IReadOnlyList<ChangeDescriptor> Applied { get; init; }
    public ChangeDescriptor? Failed { get; init; }
    public required IReadOnlyList<ChangeDescriptor> RolledBack { get; init; }
    public string? ErrorMessage { get; init; }
    public ErrorCategory? ErrorCategory { get; init; }
}
