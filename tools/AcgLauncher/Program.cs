using System.Diagnostics;
using System.Runtime.InteropServices;

[assembly: DefaultDllImportSearchPaths(DllImportSearchPath.System32)]

namespace AcgLauncher;

internal static partial class Program
{
    private const nuint ProcThreadAttributeMitigationPolicy = 0x00020007;
    private const ulong ProhibitDynamicCodeAlwaysOn = 1UL << 36;
    private const uint CreateSuspended = 0x00000004;
    private const uint ExtendedStartupInfoPresent = 0x00080000;
    private const uint StillActive = 259;
    private const uint ProcessDynamicCodePolicy = 2;
    private const uint ProhibitDynamicCode = 0x1;
    private const uint AllowThreadOptOut = 0x2;
    private const uint AllowRemoteDowngrade = 0x4;

    public static int Main(string[] args)
    {
        if (args.Length is < 1 or > 2 || (args.Length == 2 && args[1] != "--no-window"))
        {
            Console.Error.WriteLine("Usage: AcgLauncher <absolute-executable-path> [--no-window]");
            return 64;
        }

        var executablePath = Path.GetFullPath(args[0]);
        var requireWindow = args.Length == 1;
        if (!File.Exists(executablePath))
        {
            Console.Error.WriteLine($"Executable not found: {executablePath}");
            return 66;
        }

        nuint attributeListBytes = 0;
        _ = InitializeProcThreadAttributeList(0, 1, 0, ref attributeListBytes);
        if (attributeListBytes == 0)
        {
            return Fail("InitializeProcThreadAttributeList sizing");
        }

        var attributeList = Marshal.AllocHGlobal(checked((int)attributeListBytes));
        var policyValue = Marshal.AllocHGlobal(sizeof(long));
        PROCESS_INFORMATION processInfo = default;
        var childCreated = false;
        var attributeListInitialized = false;

        try
        {
            if (!InitializeProcThreadAttributeList(attributeList, 1, 0, ref attributeListBytes))
            {
                return Fail("InitializeProcThreadAttributeList");
            }
            attributeListInitialized = true;

            Marshal.WriteInt64(policyValue, unchecked((long)ProhibitDynamicCodeAlwaysOn));
            if (!UpdateProcThreadAttribute(
                    attributeList, 0, ProcThreadAttributeMitigationPolicy,
                    policyValue, sizeof(long), 0, 0))
            {
                return Fail("UpdateProcThreadAttribute");
            }

            var startupInfo = new STARTUPINFOEX
            {
                StartupInfo = new STARTUPINFO
                {
                    cb = (uint)Marshal.SizeOf<STARTUPINFOEX>(),
                },
                lpAttributeList = attributeList,
            };

            if (!CreateProcessW(
                    executablePath, 0, 0, 0, false,
                    CreateSuspended | ExtendedStartupInfoPresent,
                    0, Path.GetDirectoryName(executablePath)!,
                    ref startupInfo, out processInfo))
            {
                return Fail("CreateProcessW");
            }

            childCreated = true;
            if (!TryReadAcg(processInfo.hProcess, out var initialAcg))
            {
                return Fail("GetProcessMitigationPolicy before resume");
            }

            if (ResumeThread(processInfo.hThread) == uint.MaxValue)
            {
                return Fail("ResumeThread");
            }

            var mainWindowCreated = requireWindow
                ? WaitForMainWindow(processInfo.dwProcessId, processInfo.hProcess)
                : WaitForStartup(processInfo.hProcess);
            if (!GetExitCodeProcess(processInfo.hProcess, out var exitCode))
            {
                return Fail("GetExitCodeProcess");
            }

            if (!TryReadAcg(processInfo.hProcess, out var runningAcg))
            {
                return Fail("GetProcessMitigationPolicy after startup");
            }

            var alive = exitCode == StillActive;
            var readinessLabel = requireWindow ? "MainWindow" : "StartupReady";
            Console.WriteLine(
                $"PID={processInfo.dwProcessId}; ACGBeforeResume={initialAcg}; " +
                $"ACGAfterStartup={runningAcg}; Alive={alive}; {readinessLabel}={mainWindowCreated}; ExitCode={exitCode}");

            return initialAcg && runningAcg && alive && mainWindowCreated ? 0 : 1;
        }
        finally
        {
            if (childCreated)
            {
                _ = TerminateProcess(processInfo.hProcess, 0);
                _ = CloseHandle(processInfo.hThread);
                _ = CloseHandle(processInfo.hProcess);
            }

            if (attributeListInitialized)
            {
                DeleteProcThreadAttributeList(attributeList);
            }
            Marshal.FreeHGlobal(policyValue);
            Marshal.FreeHGlobal(attributeList);
        }
    }

    private static bool WaitForStartup(nint processHandle)
    {
        Thread.Sleep(2000);
        return GetExitCodeProcess(processHandle, out var exitCode) && exitCode == StillActive;
    }

    private static bool WaitForMainWindow(uint processId, nint processHandle)
    {
        var deadline = DateTime.UtcNow.AddSeconds(20);
        while (DateTime.UtcNow < deadline)
        {
            if (!GetExitCodeProcess(processHandle, out var exitCode) || exitCode != StillActive)
            {
                return false;
            }

            try
            {
                using var process = Process.GetProcessById(checked((int)processId));
                process.Refresh();
                if (process.MainWindowHandle != 0)
                {
                    return true;
                }
            }
            catch (ArgumentException)
            {
                return false;
            }

            Thread.Sleep(250);
        }

        return false;
    }

    private static bool TryReadAcg(nint processHandle, out bool enabled)
    {
        enabled = false;
        if (!GetProcessMitigationPolicy(
                processHandle, ProcessDynamicCodePolicy,
                out var flags, sizeof(uint)))
        {
            return false;
        }

        enabled = (flags & ProhibitDynamicCode) != 0 &&
            (flags & (AllowThreadOptOut | AllowRemoteDowngrade)) == 0;
        return true;
    }

    private static int Fail(string operation)
    {
        Console.Error.WriteLine($"{operation} failed with Win32 error {Marshal.GetLastPInvokeError()}.");
        return 1;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct STARTUPINFO
    {
        public uint cb;
        public nint lpReserved, lpDesktop, lpTitle;
        public uint dwX, dwY, dwXSize, dwYSize, dwXCountChars, dwYCountChars;
        public uint dwFillAttribute, dwFlags;
        public ushort wShowWindow, cbReserved2;
        public nint lpReserved2, hStdInput, hStdOutput, hStdError;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct STARTUPINFOEX
    {
        public STARTUPINFO StartupInfo;
        public nint lpAttributeList;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct PROCESS_INFORMATION
    {
        public nint hProcess, hThread;
        public uint dwProcessId, dwThreadId;
    }

    [LibraryImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool InitializeProcThreadAttributeList(
        nint lpAttributeList, uint dwAttributeCount, uint dwFlags, ref nuint lpSize);

    [LibraryImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool UpdateProcThreadAttribute(
        nint lpAttributeList, uint dwFlags, nuint attribute, nint lpValue,
        nuint cbSize, nint lpPreviousValue, nint lpReturnSize);

    [LibraryImport("kernel32.dll")]
    private static partial void DeleteProcThreadAttributeList(nint lpAttributeList);

    [LibraryImport("kernel32.dll", StringMarshalling = StringMarshalling.Utf16, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool CreateProcessW(
        string lpApplicationName, nint lpCommandLine,
        nint lpProcessAttributes, nint lpThreadAttributes,
        [MarshalAs(UnmanagedType.Bool)] bool bInheritHandles,
        uint dwCreationFlags, nint lpEnvironment, string lpCurrentDirectory,
        ref STARTUPINFOEX lpStartupInfo, out PROCESS_INFORMATION lpProcessInformation);

    [LibraryImport("kernel32.dll", SetLastError = true)]
    private static partial uint ResumeThread(nint hThread);

    [LibraryImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool GetProcessMitigationPolicy(
        nint hProcess, uint mitigationPolicy, out uint lpBuffer, nuint dwLength);

    [LibraryImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool GetExitCodeProcess(nint hProcess, out uint lpExitCode);

    [LibraryImport("kernel32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool TerminateProcess(nint hProcess, uint uExitCode);

    [LibraryImport("kernel32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool CloseHandle(nint hObject);
}
