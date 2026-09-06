namespace ThisIsMyPC.Core.Changes;

/// <summary>
/// One change that applied but could not be put back during mid-group rollback.
/// The descriptor is the original (apply-direction) change. Its live value is
/// uncertain either way: a revert that returned a failed result may have written
/// part of the value before failing, and a revert that threw
/// (<see cref="Exception"/> non-null) may have done the same. The exception is
/// kept for diagnostics only; a null exception is not evidence of state.
/// </summary>
public sealed record RollbackFailure(
    ChangeDescriptor Change,
    string? ErrorMessage,
    Exception? Exception);
