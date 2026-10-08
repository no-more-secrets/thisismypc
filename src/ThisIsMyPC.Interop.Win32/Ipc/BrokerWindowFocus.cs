using System.Runtime.InteropServices;

namespace ThisIsMyPC.Interop.Win32.Ipc;

/// <summary>Lets the elevated broker show its review window after an Apply click.</summary>
public static partial class BrokerWindowFocus
{
    public static bool Allow(int processId) => AllowSetForegroundWindow(processId);

    [LibraryImport("user32.dll", EntryPoint = "AllowSetForegroundWindow")]
    [return: MarshalAs(UnmanagedType.Bool)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    private static partial bool AllowSetForegroundWindow(int processId);
}
