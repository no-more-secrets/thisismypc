using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using ThisIsMyPC.Core.Services;

namespace ThisIsMyPC.Interop.Win32.Hardware;

/// <summary>Finds the exact executable in this session and activates its existing window when Windows permits it.</summary>
public sealed partial class CompanionWindowService : ICompanionWindowService
{
    public CompanionWindowOutcome TryActivate(string executablePath)
    {
        using var current = Process.GetCurrentProcess();
        var running = false;
        Process[] processes;
        try { processes = Process.GetProcessesByName(Path.GetFileNameWithoutExtension(executablePath)); }
        catch (InvalidOperationException) { return CompanionWindowOutcome.RunningWithoutAccessibleWindow; }
        try
        {
            foreach (var process in processes)
            {
                try
                {
                    if (process.SessionId != current.SessionId) continue;
                    var path = ReadImagePath(process.Id);
                    if (path is null) { running = true; continue; }
                    if (!string.Equals(path, Path.GetFullPath(executablePath), StringComparison.OrdinalIgnoreCase)) continue;
                    running = true;
                    var window = process.MainWindowHandle;
                    if (window == 0) continue;
                    if (IsIconic(window)) ShowWindowAsync(window, 9); // SW_RESTORE
                    if (SetForegroundWindow(window)) return CompanionWindowOutcome.Activated;
                }
                catch (Exception ex) when (ex is Win32Exception or InvalidOperationException)
                {
                    // An inaccessible or disappearing candidate must not trigger a duplicate launch.
                    running = true;
                }
            }
        }
        finally { foreach (var process in processes) process.Dispose(); }
        return running ? CompanionWindowOutcome.RunningWithoutAccessibleWindow : CompanionWindowOutcome.NotRunning;
    }

    private static string? ReadImagePath(int processId)
    {
        var handle = NativeHardware.OpenProcess(NativeHardware.PROCESS_QUERY_LIMITED_INFORMATION, false, (uint)processId);
        if (handle == 0) return null;
        try
        {
            Span<char> buffer = stackalloc char[1024];
            var size = (uint)buffer.Length;
            return NativeHardware.QueryFullProcessImageNameW(handle, 0, buffer, ref size) ? new string(buffer[..(int)size]) : null;
        }
        finally { NativeHardware.CloseHandle(handle); }
    }

    [LibraryImport("user32.dll")]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool SetForegroundWindow(nint window);

    [LibraryImport("user32.dll")]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool ShowWindowAsync(nint window, int command);

    [LibraryImport("user32.dll")]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool IsIconic(nint window);
}
