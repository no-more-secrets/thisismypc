using ThisIsMyPC.Core.Results;

namespace ThisIsMyPC.Core.Services;

/// <summary>The signed-in desktop user, as resolved from a token of theirs.</summary>
public sealed record InteractiveUser
{
    /// <summary>Security identifier, e.g. S-1-5-21-...; the key for HKU\{sid} work.</summary>
    public required string Sid { get; init; }

    /// <summary>DOMAIN\name where it could be resolved, else empty.</summary>
    public required string AccountName { get; init; }

    /// <summary>Interactive session the user is signed in to.</summary>
    public required uint SessionId { get; init; }
}

/// <summary>
/// Lets the app act as the signed-in desktop user rather than as the elevated
/// administrator (or SYSTEM) it runs as. The app is elevated and corresponds
/// to the PC, not a profile, so anything that touches the user's own world
/// needs the user's context: launching an app or opening a folder so it does
/// not come up elevated, writing a value into the user's registry hive, and
/// resolving the user's SID.
///
/// One interface, more than one backend: the elevated desktop app borrows a
/// token from a process the user is already running, while the Session 0
/// service (SYSTEM) can duplicate the user's token cleanly through
/// WTSQueryUserToken. Consumers depend on this, not on either mechanism.
/// </summary>
public interface IInteractiveUserContext
{
    /// <summary>True when the calling process is elevated, so acting as the user takes real work.</summary>
    bool IsCallerElevated { get; }

    /// <summary>The signed-in desktop user, or null when none can be resolved.</summary>
    InteractiveUser? Current { get; }

    /// <summary>
    /// Starts a process as the desktop user, never elevated. When the caller
    /// is not elevated it is already that user, so the process is started
    /// plainly.
    /// </summary>
    OperationResult<bool> LaunchAsUser(string applicationPath, string? arguments = null);

    /// <summary>
    /// Runs <paramref name="action"/> as the desktop user, so the registry,
    /// file, and API calls inside it act as that user (HKCU is the user's
    /// hive). Impersonation is per-thread, so the action runs inline on the
    /// calling thread and must not hop threads or await; keep it a tight,
    /// synchronous unit of work.
    /// </summary>
    OperationResult<T> RunAsUser<T>(Func<T> action);
}
