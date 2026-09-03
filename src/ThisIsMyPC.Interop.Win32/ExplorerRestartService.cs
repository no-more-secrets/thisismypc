using System.Diagnostics;
using ThisIsMyPC.Core.Results;
using ThisIsMyPC.Core.Services;
using Windows.Win32;
using Windows.Win32.UI.Shell;

namespace ThisIsMyPC.Interop.Win32;

/// <summary>
/// Restarts the shell. First choice is the Restart Manager's forced
/// shutdown of the shell process, which ExplorerPatcher uses too and takes
/// well under a second. The old route (WM_QUIT to the tray, wait, kill)
/// stays as the fallback when the manager refuses. Either way the new
/// explorer.exe is started by <see cref="ShellLauncher"/> as the desktop
/// user, never with this process's elevated token: RmRestart from an
/// elevated caller, like a plain Process.Start, brings the shell back
/// elevated, and ExplorerPatcher's own taskbar never appears in an elevated
/// Explorer (Sam's PC, 2026-09-03).
/// </summary>
public sealed class ExplorerRestartService : IExplorerRestartService
{
    private static readonly NLog.Logger Log = NLog.LogManager.GetLogger("ThisIsMyPC.Interop.Win32.ExplorerRestartService");
    private const uint WM_QUIT = 0x0012;
    private static readonly TimeSpan GracefulTimeout = TimeSpan.FromSeconds(5);
    private static readonly TimeSpan ShellRecoveryTimeout = TimeSpan.FromSeconds(10);
    private static readonly TimeSpan ShellPollInterval = TimeSpan.FromMilliseconds(250);

    public async Task<OperationResult<bool>> RestartExplorerAsync()
    {
        try
        {
            // 1. Find the shell tray window (owned by the shell explorer.exe process)
            var trayHandle = PInvoke.FindWindow("Shell_TrayWnd", null);
            if (trayHandle.IsNull)
            {
                return OperationResult<bool>.Failure(
                    "Could not find the Explorer shell tray window (Shell_TrayWnd). Explorer may not be running.",
                    ErrorCategory.ServiceUnavailable);
            }

            // 2. Identify the shell process before sending quit
            using var shellProcess = GetProcessFromWindow(trayHandle);
            if (shellProcess is null)
            {
                return OperationResult<bool>.Failure(
                    "Could not identify the Explorer shell process.",
                    ErrorCategory.ServiceUnavailable);
            }

            var clock = Stopwatch.StartNew();
            var managed = await Task.Run(() => ShutDownWithRestartManager(shellProcess)).ConfigureAwait(false);
            if (managed.IsSuccess)
            {
                var started = ShellLauncher.StartExplorerAsDesktopUser();
                if (!started.IsSuccess)
                    return started;
                var back = await WaitForShellRecoveryAsync(ShellRecoveryTimeout).ConfigureAwait(false);
                Log.Info("Explorer restarted through the Restart Manager in {Ms} ms (taskbar back: {Back})", clock.ElapsedMilliseconds, back);
                return back ? OperationResult<bool>.Success(true) : ShellDidNotComeBack();
            }
            Log.Warn("Restart Manager could not shut Explorer down ({Error}); falling back to the tray quit", managed.ErrorMessage);

            // 3. Send WM_QUIT for graceful shutdown
            PInvoke.PostMessage(trayHandle, WM_QUIT, 0, 0);

            // 4. Wait for graceful exit
            var exited = await WaitForExitAsync(shellProcess, GracefulTimeout).ConfigureAwait(false);

            // 5. Force-kill if graceful shutdown failed
            if (!exited)
            {
                try
                {
                    shellProcess.Kill();
                    await WaitForExitAsync(shellProcess, TimeSpan.FromSeconds(2)).ConfigureAwait(false);
                }
                catch (InvalidOperationException)
                {
                    // Process already exited between check and kill; that's fine
                }
            }

            // 6. Start the new explorer.exe as the desktop user
            var launched = ShellLauncher.StartExplorerAsDesktopUser();
            if (!launched.IsSuccess)
                return launched;

            // 7. Poll for Shell_TrayWnd to reappear (shell is ready when taskbar is back)
            var recovered = await WaitForShellRecoveryAsync(ShellRecoveryTimeout).ConfigureAwait(false);
            Log.Info("Explorer restarted through the tray quit in {Ms} ms (taskbar back: {Back})", clock.ElapsedMilliseconds, recovered);

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
