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
    private const uint PrintWindowRenderFullContent = 0x2;

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

            nint mainWindow = 0;
            var mainWindowCreated = requireWindow
                ? WaitForMainWindow(processInfo.dwProcessId, processInfo.hProcess, out mainWindow)
                : WaitForStartup(processInfo.hProcess);
            var windowRendered = !requireWindow ||
                mainWindowCreated && WaitForRenderedWindow(mainWindow);
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
                $"ACGAfterStartup={runningAcg}; Alive={alive}; {readinessLabel}={mainWindowCreated}; " +
                $"Rendered={windowRendered}; ExitCode={exitCode}");

            return initialAcg && runningAcg && alive && mainWindowCreated && windowRendered ? 0 : 1;
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

    private static bool WaitForMainWindow(uint processId, nint processHandle, out nint windowHandle)
    {
        windowHandle = 0;
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
                    windowHandle = process.MainWindowHandle;
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

    private static bool WaitForRenderedWindow(nint windowHandle)
    {
        var deadline = DateTime.UtcNow.AddSeconds(10);
        while (DateTime.UtcNow < deadline)
        {
            if (HasVisiblePixels(windowHandle))
            {
                return true;
            }

            Thread.Sleep(250);
        }

        return false;
    }

    private static unsafe bool HasVisiblePixels(nint windowHandle)
    {
        if (!GetWindowRect(windowHandle, out var windowRect) ||
            !GetClientRect(windowHandle, out var clientRect))
        {
            return false;
        }

        var clientOrigin = new POINT();
        if (!ClientToScreen(windowHandle, ref clientOrigin))
        {
            return false;
        }

        var width = windowRect.Right - windowRect.Left;
        var height = windowRect.Bottom - windowRect.Top;
        var clientWidth = clientRect.Right - clientRect.Left;
        var clientHeight = clientRect.Bottom - clientRect.Top;
        var clientX = clientOrigin.X - windowRect.Left;
        var clientY = clientOrigin.Y - windowRect.Top;
        if (width <= 0 || height <= 0 || clientWidth <= 0 || clientHeight <= 0 ||
            clientX < 0 || clientY < 0 || clientX + clientWidth > width ||
            clientY + clientHeight > height)
        {
            return false;
        }

        var screenDc = GetDC(0);
        if (screenDc == 0)
        {
            return false;
        }

        var memoryDc = CreateCompatibleDC(screenDc);
        nint bitmap = 0;
        nint oldBitmap = 0;
        try
        {
            var bitmapInfo = new BITMAPINFO
            {
                Header = new BITMAPINFOHEADER
                {
                    Size = (uint)Marshal.SizeOf<BITMAPINFOHEADER>(),
                    Width = width,
                    Height = -height,
                    Planes = 1,
                    BitCount = 32,
                },
            };
            bitmap = CreateDIBSection(screenDc, ref bitmapInfo, 0, out var pixels, 0, 0);
            if (memoryDc == 0 || bitmap == 0 || pixels == 0)
            {
                return false;
            }

            var pixelCount = checked(width * height);
            new Span<byte>((void*)pixels, checked(pixelCount * sizeof(uint))).Clear();
            oldBitmap = SelectObject(memoryDc, bitmap);
            if (!PrintWindow(windowHandle, memoryDc, PrintWindowRenderFullContent))
            {
                return false;
            }

            var sampled = 0;
            var visible = 0;
            var inset = clientWidth > 16 && clientHeight > 16 ? 4 : 0;
            for (var y = inset; y < clientHeight - inset; y += 4)
            {
                for (var x = inset; x < clientWidth - inset; x += 4)
                {
                    var index = checked((clientY + y) * width + clientX + x);
                    var color = unchecked((uint)Marshal.ReadInt32(pixels, index * sizeof(uint)));
                    var red = (color >> 16) & 0xff;
                    var green = (color >> 8) & 0xff;
                    var blue = color & 0xff;
                    sampled++;
                    if (red > 32 || green > 32 || blue > 32)
                    {
                        visible++;
                    }
                }
            }

            return visible >= Math.Max(1, sampled / 50);
        }
        finally
        {
            if (oldBitmap != 0)
            {
                _ = SelectObject(memoryDc, oldBitmap);
            }
            if (bitmap != 0)
            {
                _ = DeleteObject(bitmap);
            }
            if (memoryDc != 0)
            {
                _ = DeleteDC(memoryDc);
            }
            _ = ReleaseDC(0, screenDc);
        }
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

    [StructLayout(LayoutKind.Sequential)]
    private struct RECT
    {
        public int Left, Top, Right, Bottom;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct POINT
    {
        public int X, Y;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct BITMAPINFOHEADER
    {
        public uint Size;
        public int Width, Height;
        public ushort Planes, BitCount;
        public uint Compression, SizeImage;
        public int XPelsPerMeter, YPelsPerMeter;
        public uint ColorsUsed, ColorsImportant;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct BITMAPINFO
    {
        public BITMAPINFOHEADER Header;
        public uint Colors;
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

    [LibraryImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool GetWindowRect(nint hWnd, out RECT lpRect);

    [LibraryImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool GetClientRect(nint hWnd, out RECT lpRect);

    [LibraryImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool ClientToScreen(nint hWnd, ref POINT lpPoint);

    [LibraryImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool PrintWindow(nint hWnd, nint hdcBlt, uint nFlags);

    [LibraryImport("user32.dll")]
    private static partial nint GetDC(nint hWnd);

    [LibraryImport("user32.dll")]
    private static partial int ReleaseDC(nint hWnd, nint hDC);

    [LibraryImport("gdi32.dll")]
    private static partial nint CreateCompatibleDC(nint hdc);

    [LibraryImport("gdi32.dll")]
    private static partial nint CreateDIBSection(
        nint hdc, ref BITMAPINFO pbmi, uint usage, out nint bits, nint section, uint offset);

    [LibraryImport("gdi32.dll")]
    private static partial nint SelectObject(nint hdc, nint hObject);

    [LibraryImport("gdi32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool DeleteObject(nint hObject);

    [LibraryImport("gdi32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool DeleteDC(nint hdc);
}
