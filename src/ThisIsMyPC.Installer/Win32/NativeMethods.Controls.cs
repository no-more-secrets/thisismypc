using System.Runtime.InteropServices;

namespace ThisIsMyPC.Installer.Win32;

internal static unsafe partial class NativeMethods
{
    internal const uint WM_SIZE = 5;
    internal const uint WM_VSCROLL = 0x115;
    internal const uint WM_HSCROLL = 0x114;
    internal const uint WM_MOUSEWHEEL = 0x20A;
    internal const uint WM_CTLCOLORBTN = 0x135;
    internal const uint BM_GETCHECK = 0xF0;
    internal const uint BM_SETCHECK = 0xF1;
    internal const uint BM_CLICK = 0xF5;
    internal const uint WM_PRINT = 0x317;

    [StructLayout(LayoutKind.Sequential)]
    internal struct SCROLLINFO
    {
        internal uint cbSize;
        internal uint fMask;
        internal int nMin;
        internal int nMax;
        internal uint nPage;
        internal int nPos;
        internal int nTrackPos;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct MONITORINFO
    {
        internal uint cbSize;
        internal RECT rcMonitor;
        internal RECT rcWork;
        internal uint dwFlags;
    }

    [LibraryImport("user32.dll")]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    internal static partial nint MonitorFromWindow(nint hwnd, uint flags);

    [LibraryImport("user32.dll")]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    internal static partial nint MonitorFromRect(in RECT rect, uint flags);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    internal static partial bool IsWindowVisible(nint hwnd);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    internal static partial bool IsWindowEnabled(nint hwnd);

    [LibraryImport("user32.dll", EntryPoint = "GetMonitorInfoW")]
    [return: MarshalAs(UnmanagedType.Bool)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    internal static partial bool GetMonitorInfo(nint monitor, ref MONITORINFO info);

    [LibraryImport("user32.dll")]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    internal static partial int SetScrollInfo(nint hwnd, int bar, in SCROLLINFO info, [MarshalAs(UnmanagedType.Bool)] bool redraw);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    internal static partial bool GetScrollInfo(nint hwnd, int bar, ref SCROLLINFO info);

    [LibraryImport("gdi32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    internal static partial bool SetViewportOrgEx(nint dc, int x, int y, nint previous);

    [LibraryImport("gdi32.dll")]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    internal static partial int SaveDC(nint dc);

    [LibraryImport("gdi32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    internal static partial bool RestoreDC(nint dc, int saved);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    internal static partial bool GetWindowRect(nint hwnd, out RECT rect);

    [LibraryImport("user32.dll")]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    internal static partial nint GetDlgItem(nint hwnd, int id);
    [StructLayout(LayoutKind.Sequential)]
    internal struct HIGHCONTRAST
    {
        internal uint cbSize;
        internal uint dwFlags;
        internal nint lpszDefaultScheme;
    }

    [LibraryImport("user32.dll", EntryPoint = "SystemParametersInfoW")]
    [return: MarshalAs(UnmanagedType.Bool)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    internal static partial bool GetHighContrast(uint action, uint size, ref HIGHCONTRAST value, uint flags);

    [LibraryImport("comctl32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    internal static partial bool SetWindowSubclass(nint hwnd, nint callback, nuint id, nuint data);

    [LibraryImport("comctl32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    internal static partial bool RemoveWindowSubclass(nint hwnd, nint callback, nuint id);

    [LibraryImport("comctl32.dll")]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    internal static partial nint DefSubclassProc(nint hwnd, uint message, nuint wParam, nint lParam);
}
