using ThisIsMyPC.Core.Results;

namespace ThisIsMyPC.Core.Enforcement;

public record EnforcementResult
{
    public bool IsSuccess { get; init; }
    public IReadOnlyList<EnforcementStepResult> Steps { get; init; } = [];
    public string? ErrorMessage { get; init; }
    public ErrorCategory? ErrorCategory { get; init; }
}
