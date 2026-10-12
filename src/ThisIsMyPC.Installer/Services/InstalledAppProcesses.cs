using System.Diagnostics;
using System.ComponentModel;

namespace ThisIsMyPC.Installer.Services;

internal interface IInstalledAppProcess : IDisposable
{
    string ImagePath { get; }
    bool HasExited { get; }
    void RequestClose();
    void Kill();
    Task WaitForExitAsync(CancellationToken cancellationToken);
}

internal static class InstalledAppProcesses
{
    private static readonly string[] Names =
        ["ThisIsMyPC.App", "ThisIsMyPC.Broker", "ThisIsMyPC (administrator access)", "ThisIsMyPC-LightingEngine"];

    internal static async Task StopAsync(IEnumerable<string> installFolders, CancellationToken cancellationToken)
    {
        var processes = new List<RunningProcess>();
        try
        {
            foreach (var process in Names.SelectMany(Process.GetProcessesByName))
            {
                var pinned = RunningProcess.TryCreate(process);
                if (pinned is not null) processes.Add(pinned);
            }
            await StopAsync(processes, installFolders, TimeSpan.FromSeconds(5), cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            foreach (var process in processes) process.Dispose();
        }
    }

    internal static async Task StopAsync(
        IEnumerable<IInstalledAppProcess> processes, IEnumerable<string> installFolders,
        TimeSpan gracePeriod, CancellationToken cancellationToken)
    {
        var folders = installFolders.Select(Path.GetFullPath).ToArray();
        var targets = new List<IInstalledAppProcess>();
        foreach (var process in processes)
        {
            try
            {
                if (!process.HasExited && IsTarget(process.ImagePath, folders)) targets.Add(process);
            }
            catch (Exception ex) when ((ex is InvalidOperationException or Win32Exception) && process.HasExited)
            {
                // A process that stopped during identification needs no shutdown.
            }
        }
        foreach (var process in targets)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!process.HasExited) process.RequestClose();
        }

        using var graceful = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        graceful.CancelAfter(gracePeriod);
        try
        {
            await Task.WhenAll(targets.Select(p => p.WaitForExitAsync(graceful.Token))).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            // Tray mode can turn a close request into hiding the window.
        }
        cancellationToken.ThrowIfCancellationRequested();
        foreach (var process in targets)
            if (!process.HasExited) process.Kill();

        using var stopped = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        stopped.CancelAfter(TimeSpan.FromSeconds(10));
        try
        {
            await Task.WhenAll(targets.Select(p => p.WaitForExitAsync(stopped.Token))).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            throw new InvalidOperationException("Close ThisIsMyPC and try again. A running copy could not be stopped.");
        }
    }

    internal static bool IsTarget(string imagePath, IEnumerable<string> installFolders)
    {
        var path = Path.GetFullPath(imagePath);
        if (!Names.Any(name => Path.GetFileName(path).Equals(name + ".exe", StringComparison.OrdinalIgnoreCase)))
            return false;
        return installFolders.Any(folder => path.StartsWith(
            Path.TrimEndingDirectorySeparator(Path.GetFullPath(folder)) + Path.DirectorySeparatorChar,
            StringComparison.OrdinalIgnoreCase));
    }

    private sealed class RunningProcess(Process process) : IInstalledAppProcess
    {
        internal static RunningProcess? TryCreate(Process process)
        {
            try
            {
                // Retain the handle before any PID-based image lookup. It prevents PID reuse.
                _ = process.SafeHandle;
                return new RunningProcess(process);
            }
            catch (InvalidOperationException)
            {
                process.Dispose();
                return null;
            }
            catch (Win32Exception ex) when (ex.NativeErrorCode == 87)
            {
                process.Dispose();
                return null;
            }
            catch
            {
                process.Dispose();
                throw;
            }
        }
        public string ImagePath => process.MainModule?.FileName
            ?? throw new InvalidOperationException("The running ThisIsMyPC copy could not be identified.");
        public bool HasExited => process.HasExited;
        public void RequestClose()
        {
            try { process.CloseMainWindow(); }
            catch (InvalidOperationException) when (process.HasExited) { }
        }
        public void Kill()
        {
            try { process.Kill(); }
            catch (InvalidOperationException) when (process.HasExited) { }
        }
        public Task WaitForExitAsync(CancellationToken cancellationToken) => process.WaitForExitAsync(cancellationToken);
        public void Dispose() => process.Dispose();
    }
}
