namespace ThisIsMyPC.Core.Enforcement;

public record EnforcementStepResult
{
    public required EnforcementStepType StepType { get; init; }
    public required string Target { get; init; }
    public required bool IsSuccess { get; init; }
    public string? ErrorMessage { get; init; }
    public bool WasRolledBack { get; init; }
}
