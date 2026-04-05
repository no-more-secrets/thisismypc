using System.Diagnostics;
using ThisIsMyPC.Core.Results;
using ThisIsMyPC.Core.Services;
using Windows.Win32;
using Windows.Win32.UI.Shell;

namespace ThisIsMyPC.Interop.Win32;

public sealed class ExplorerRestartService : IExplorerRestartService
{
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
                    // Process already exited between check and kill — that's fine
                }
            }

            // 6. Start new explorer.exe (use full path to prevent PATH hijacking)
            var explorerPath = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.Windows),
                "explorer.exe");
            Process.Start(new ProcessStartInfo
            {
                FileName = explorerPath,
                UseShellExecute = false,
            });

            // 7. Poll for Shell_TrayWnd to reappear (shell is ready when taskbar is back)
            var recovered = await WaitForShellRecoveryAsync(ShellRecoveryTimeout).ConfigureAwait(false);
            if (!recovered)
            {
                Debug.WriteLine("Shell_TrayWnd did not reappear within timeout — Explorer may be starting slowly");
            }

            return OperationResult<bool>.Success(true);
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
            Debug.WriteLine($"SHChangeNotify failed: {ex.Message}");
            return OperationResult<bool>.Failure(
                $"Failed to refresh Explorer views: {ex.Message}",
                ErrorCategory.ServiceUnavailable, ex);
        }
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
