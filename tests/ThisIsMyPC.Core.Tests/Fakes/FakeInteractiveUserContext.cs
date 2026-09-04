using ThisIsMyPC.Core.Results;
using ThisIsMyPC.Core.Services;

namespace ThisIsMyPC.Core.Tests.Fakes;

/// <summary>
/// A test double for <see cref="IInteractiveUserContext"/>: runs actions on
/// the calling thread with no impersonation, records what was launched, and
/// answers as an unelevated caller unless told otherwise.
/// </summary>
public sealed class FakeInteractiveUserContext : IInteractiveUserContext
{
    public bool IsCallerElevated { get; init; }

    public InteractiveUser? Current { get; init; } =
        new() { Sid = "S-1-5-21-0-0-0-1000", AccountName = @"PC\tester", SessionId = 1 };

    /// <summary>Each (path, args) passed to <see cref="LaunchAsUser"/>, in order.</summary>
    public List<(string Path, string? Arguments)> Launched { get; } = [];

    /// <summary>Set to make <see cref="LaunchAsUser"/> report failure.</summary>
    public bool LaunchShouldSucceed { get; init; } = true;

    public OperationResult<bool> LaunchAsUser(string applicationPath, string? arguments = null)
    {
        Launched.Add((applicationPath, arguments));
        return LaunchShouldSucceed
            ? OperationResult<bool>.Success(true)
            : OperationResult<bool>.Failure("Fake launch failure", ErrorCategory.ServiceUnavailable);
    }

    public OperationResult<T> RunAsUser<T>(Func<T> action)
    {
        ArgumentNullException.ThrowIfNull(action);
        return OperationResult<T>.Success(action());
    }
}
