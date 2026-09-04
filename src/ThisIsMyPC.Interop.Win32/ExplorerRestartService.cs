using System.Diagnostics;
using ThisIsMyPC.Core.Results;
using ThisIsMyPC.Core.Services;
using Windows.Win32;
using Windows.Win32.UI.Shell;

namespace ThisIsMyPC.Interop.Win32;

/// <summary>
/// Restarts the shell by terminating explorer.exe and starting a new one.
/// The app is elevated, so it can force-kill the shell at once; the Restart
/// Manager is the fallback for the rare refused kill. Its graceful shutdown
/// is not the first choice because its window messages cannot cross UIPI
/// from an elevated caller to the unelevated shell, so it waits its full
/// ~30 s timeout before force-killing anyway (Sam's log, 2026-09-03).
/// Either way the new explorer.exe is started by <see cref="IInteractiveUserContext.LaunchAsUser"/>
/// as the desktop user, never with this process's elevated token: a shell
/// started elevated elevates every app opened from Start, and
/// ExplorerPatcher's own taskbar never appears in an elevated Explorer.
/// </summary>
public sealed class ExplorerRestartService : IExplorerRestartService
{
    private static readonly NLog.Logger Log = NLog.LogManager.GetLogger("ThisIsMyPC.Interop.Win32.ExplorerRestartService");
    private static readonly TimeSpan KillTimeout = TimeSpan.FromSeconds(5);
    private static readonly TimeSpan AutoRespawnGrace = TimeSpan.FromSeconds(2);
    private static readonly TimeSpan ShellRecoveryTimeout = TimeSpan.FromSeconds(10);
    private static readonly TimeSpan ShellPollInterval = TimeSpan.FromMilliseconds(250);

    private readonly IInteractiveUserContext _userContext;

    public ExplorerRestartService(IInteractiveUserContext userContext)
    {
        ArgumentNullException.ThrowIfNull(userContext);
        _userContext = userContext;
    }

    private static readonly string ExplorerPath =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "explorer.exe");

    public async Task<OperationResult<bool>> RestartExplorerAsync()
    {
        try
        {
            var clock = Stopwatch.StartNew();

            // The running shell, if any. None means there is nothing to stop, so
            // a shell is started rather than restarted.
            var trayHandle = PInvoke.FindWindow("Shell_TrayWnd", null);
            using var shellProcess = trayHandle.IsNull ? null : GetProcessFromWindow(trayHandle);

            if (shellProcess is not null)
            {
                // Terminate the shell directly. The app is elevated, so it can,
                // and this avoids the Restart Manager's graceful shutdown, whose
                // window messages an elevated caller cannot deliver to the
                // unelevated shell (UIPI): it then waits its full ~30 s timeout
                // before force-killing anyway (Sam's log, 2026-09-03 21:21).
                if (!await TerminateShellAsync(shellProcess).ConfigureAwait(false))
                {
                    // A refused kill is rare; fall back to the Restart Manager,
                    // which force-kills after its wait, slow but sure.
                    Log.Warn("Could not terminate the shell directly; falling back to the Restart Manager");
                    var managed = await Task.Run(() => ShutDownWithRestartManager(shellProcess)).ConfigureAwait(false);
                    if (!managed.IsSuccess)
                        return managed;
                }

                // Killing the shell can leave the desktop user's own relaunch to
                // Windows; give it a moment, and only start one if none appears.
                if (await WaitForShellRecoveryAsync(AutoRespawnGrace).ConfigureAwait(false))
                {
                    Log.Info("Explorer came back on its own in {Ms} ms after the shell was stopped", clock.ElapsedMilliseconds);
                    return OperationResult<bool>.Success(true);
                }
            }

            var launched = _userContext.LaunchAsUser(ExplorerPath);
            if (!launched.IsSuccess)
                return launched;

            var recovered = await WaitForShellRecoveryAsync(ShellRecoveryTimeout).ConfigureAwait(false);
            Log.Info("Explorer restarted in {Ms} ms (taskbar back: {Back})", clock.ElapsedMilliseconds, recovered);
            return recovered ? OperationResult<bool>.Success(true) : ShellDidNotComeBack();
        }
        catch (Exception ex)
        {
            return OperationResult<bool>.Failure(
                $"Failed to restart Explorer: {ex.Message}",
                ErrorCategory.ServiceUnavailable,
                ex);
        }
    }

    public async Task<OperationResult<bool>> RefreshExplorerViewsAsync()
    {
        try
        {
            await Task.Run(() =>
            {
                unsafe
                {
                    PInvoke.SHChangeNotify(SHCNE_ID.SHCNE_ASSOCCHANGED, SHCNF_FLAGS.SHCNF_IDLIST, null, null);
                }
            }).ConfigureAwait(false);

            return OperationResult<bool>.Success(true);
        }
        catch (Exception ex)
        {
            Log.Warn(ex, "SHChangeNotify failed");
            return OperationResult<bool>.Failure(
                $"Failed to refresh Explorer views: {ex.Message}",
                ErrorCategory.ServiceUnavailable, ex);
        }
    }

    /// <summary>
    /// One Restart Manager session around the shell process: register it,
    /// confirm no reboot is needed, force shutdown. Any refusal is returned
    /// as a failure so the caller can fall back. The session's RmRestart is
    /// not used: it would start the shell with this process's elevated
    /// token, so the caller starts it as the desktop user instead.
    /// </summary>
    private static unsafe OperationResult<bool> ShutDownWithRestartManager(Process shellProcess)
    {
        var key = stackalloc char[NativeRestartManager.CCH_RM_SESSION_KEY + 1];
        var rc = NativeRestartManager.RmStartSession(out var session, 0, key);
        if (rc != NativeRestartManager.ERROR_SUCCESS)
            return OperationResult<bool>.Failure($"RmStartSession returned {rc}", ErrorCategory.ServiceUnavailable);

        try
        {
            var startTime = shellProcess.StartTime.ToFileTime();
            var app = new NativeRestartManager.RM_UNIQUE_PROCESS
            {
                dwProcessId = (uint)shellProcess.Id,
                ProcessStartTimeLow = (uint)(startTime & 0xFFFFFFFF),
                ProcessStartTimeHigh = (uint)(startTime >> 32),
            };
            rc = NativeRestartManager.RmRegisterResources(session, 0, 0, 1, &app, 0, 0);
            if (rc != NativeRestartManager.ERROR_SUCCESS)
                return OperationResult<bool>.Failure($"RmRegisterResources returned {rc}", ErrorCategory.ServiceUnavailable);

            uint count = 16;
            var affected = stackalloc NativeRestartManager.RM_PROCESS_INFO[16];
            rc = NativeRestartManager.RmGetList(session, out _, ref count, affected, out var rebootReason);
            if (rc != NativeRestartManager.ERROR_SUCCESS && rc != NativeRestartManager.ERROR_MORE_DATA)
                return OperationResult<bool>.Failure($"RmGetList returned {rc}", ErrorCategory.ServiceUnavailable);
            if (rebootReason != NativeRestartManager.RmRebootReasonNone)
                return OperationResult<bool>.Failure($"Restart Manager wants a reboot (reason {rebootReason})", ErrorCategory.RequiresRestart);

            rc = NativeRestartManager.RmShutdown(session, NativeRestartManager.RmForceShutdown, 0);
            if (rc != NativeRestartManager.ERROR_SUCCESS)
                return OperationResult<bool>.Failure($"RmShutdown returned {rc}", ErrorCategory.ServiceUnavailable);

            return OperationResult<bool>.Success(true);
        }
        finally
        {
            _ = NativeRestartManager.RmEndSession(session);
        }
    }

    /// <summary>
    /// The shell process was replaced but no taskbar window appeared in time.
    /// A shell extension that fails on this build (ExplorerPatcher's own
    /// taskbar on an untested Windows build, say) looks exactly like this, so
    /// it is reported as a failure the person can act on, not as success.
    /// </summary>
    private static OperationResult<bool> ShellDidNotComeBack()
    {
        Log.Warn("Shell_TrayWnd did not reappear within {Seconds} s after the restart", ShellRecoveryTimeout.TotalSeconds);
        return OperationResult<bool>.Failure(
            $"Explorer was restarted, but its taskbar has not come back after {ShellRecoveryTimeout.TotalSeconds:0} seconds. "
            + "A setting that changes the taskbar may not work on this Windows build; undo the last change from History and restart Explorer again.",
            ErrorCategory.ServiceUnavailable);
    }

    /// <summary>
    /// Force-terminates the shell process and waits for it to exit. True when
    /// it is gone. An elevated app can terminate the desktop user's shell, and
    /// a clean termination does not auto-respawn on Windows 10 and 11.
    /// </summary>
    private static async Task<bool> TerminateShellAsync(Process shellProcess)
    {
        try
        {
            shellProcess.Kill();
        }
        catch (InvalidOperationException)
        {
            return true;   // already gone
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or NotSupportedException)
        {
            Log.Warn(ex, "Kill on the shell process ({Pid}) was refused", shellProcess.Id);
            return false;
        }
        return await WaitForExitAsync(shellProcess, KillTimeout).ConfigureAwait(false);
    }

    private static async Task<bool> WaitForShellRecoveryAsync(TimeSpan timeout)
    {
        var start = Stopwatch.GetTimestamp();
        while (Stopwatch.GetElapsedTime(start) < timeout)
        {
            var trayHandle = PInvoke.FindWindow("Shell_TrayWnd", null);
            if (!trayHandle.IsNull)
                return true;

            await Task.Delay(ShellPollInterval).ConfigureAwait(false);
        }
        return false;
    }

    private static Process? GetProcessFromWindow(Windows.Win32.Foundation.HWND hwnd)
    {
        uint processId;
        unsafe
        {
            _ = PInvoke.GetWindowThreadProcessId(hwnd, &processId);
        }

        if (processId == 0)
            return null;

        try
        {
            return Process.GetProcessById((int)processId);
        }
        catch (ArgumentException)
        {
            return null;
        }
    }

    private static async Task<bool> WaitForExitAsync(Process process, TimeSpan timeout)
    {
        try
        {
            using var cts = new CancellationTokenSource(timeout);
            await process.WaitForExitAsync(cts.Token).ConfigureAwait(false);
            return true;
        }
        catch (OperationCanceledException)
        {
            return false;
        }
    }
}
