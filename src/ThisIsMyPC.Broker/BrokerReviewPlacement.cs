using System.Runtime.InteropServices;

namespace ThisIsMyPC.Broker;

// The broker locates the verified UI process itself. No window handle or position comes from IPC.
internal static partial class BrokerReviewPlacement
{
    private const string MainWindowTitle = "ThisIsMyPC";
    private const int ApplyRightInset = 41;
    private const int ReviewButtonRightInset = 16;
    private const int ApplyBarTopInset = 74;
    private const int ReviewGap = 8;

    internal readonly record struct Position(int X, int Y);
    internal readonly record struct WorkArea(int Left, int Top, int Right, int Bottom)
    {
        internal int Width => Right - Left;
        internal int Height => Bottom - Top;
    }

    internal static uint DpiForUiProcess(int uiProcessId)
    {
        var window = uiProcessId > 0 ? FindUiWindow(uiProcessId) : 0;
        var dpi = window != 0 ? GetDpiForWindow(window) : GetDpiForSystem();
        return dpi == 0 ? 96u : dpi;
    }

    internal static WorkArea? WorkAreaForUiProcess(int uiProcessId)
    {
        var window = uiProcessId > 0 ? FindUiWindow(uiProcessId) : 0;
        return window == 0 ? null : WorkAreaForMonitor(MonitorFromWindow(window, 2));
    }

    internal static WorkArea? WorkAreaForRect(int left, int top, int right, int bottom)
    {
        var bounds = new Rect { Left = left, Top = top, Right = right, Bottom = bottom };
        return WorkAreaForMonitor(MonitorFromRect(in bounds, 2));
    }

    private static WorkArea? WorkAreaForMonitor(nint monitor)
    {
        var info = new MonitorInfo { Size = (uint)Marshal.SizeOf<MonitorInfo>() };
        return monitor != 0 && GetMonitorInfoW(monitor, ref info)
            ? new WorkArea(info.Work.Left, info.Work.Top, info.Work.Right, info.Work.Bottom)
            : null;
    }

    internal static Position? ForUiProcess(int uiProcessId, int width, int height, string? uiSid = null)
    {
        if (uiProcessId <= 0 || width <= 0 || height <= 0)
            return null;

        var uiWindow = FindUiWindow(uiProcessId);
        if (uiWindow == 0 || !GetClientRect(uiWindow, out var client))
            return null;
        var clientOrigin = new Point();
        if (!ClientToScreen(uiWindow, ref clientOrigin))
            return null;

        var monitor = MonitorFromWindow(uiWindow, 2);
        var monitorInfo = new MonitorInfo { Size = (uint)Marshal.SizeOf<MonitorInfo>() };
        if (monitor == 0 || !GetMonitorInfoW(monitor, ref monitorInfo))
            return null;

        // MainWindow's Apply bar ends 24 logical pixels from the client edge.
        // Its Apply button ends 17 pixels inside that bar. The review button ends 16 pixels inside this window.
        // The bar starts 74 logical pixels above the client bottom; the popup has an 8 pixel gap.
        // The broker is per-monitor DPI aware, so these coordinates are physical pixels.
        var dpi = GetDpiForWindow(uiWindow);
        var reviewScale = dpi == 0 ? 1.0 : Math.Clamp(dpi / 96.0, 0.75, 4.0);
        var uiScale = reviewScale * BrokerReviewZoom.ForUiSid(uiSid);
        var appRight = checked(clientOrigin.X + client.Right - client.Left);
        var appBottom = checked(clientOrigin.Y + client.Bottom - client.Top);
        if (IsZoomed(uiWindow))
        {
            // Avalonia's OffScreenMargin removes the maximized frame outside the work area.
            appRight = Math.Min(appRight, monitorInfo.Work.Right);
            appBottom = Math.Min(appBottom, monitorInfo.Work.Bottom);
        }
        return Align(appRight, appBottom, uiScale, reviewScale, width, height,
            monitorInfo.Work.Left, monitorInfo.Work.Top, monitorInfo.Work.Right, monitorInfo.Work.Bottom);
    }

    internal static Position Align(int appRight, int appBottom, double uiScale, double reviewScale, int width, int height,
        int workLeft, int workTop, int workRight, int workBottom)
    {
        var x = checked(appRight - (int)Math.Round(ApplyRightInset * uiScale)
            + (int)Math.Round(ReviewButtonRightInset * reviewScale) - width);
        var y = checked(appBottom - (int)Math.Round(ApplyBarTopInset * uiScale)
            - (int)Math.Round(ReviewGap * reviewScale) - height);
        return new Position(
            Math.Clamp(x, workLeft, Math.Max(workLeft, workRight - width)),
            Math.Clamp(y, workTop, Math.Max(workTop, workBottom - height)));
    }

    private static nint FindUiWindow(int uiProcessId)
    {
        nint window = 0;
        for (var attempt = 0; attempt < 512; attempt++)
        {
            window = FindWindowExW(0, window, null, MainWindowTitle);
            if (window == 0)
                return 0;
            _ = GetWindowThreadProcessId(window, out var processId);
            if (processId == (uint)uiProcessId && IsWindowVisible(window) && !IsIconic(window)
                && GetWindow(window, 4) == 0)
                return window;
        }
        return 0;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct Point { internal int X; internal int Y; }
    [StructLayout(LayoutKind.Sequential)]
    private struct Rect { internal int Left; internal int Top; internal int Right; internal int Bottom; }
    [StructLayout(LayoutKind.Sequential)]
    private struct MonitorInfo
    {
        internal uint Size;
        internal Rect Monitor;
        internal Rect Work;
        internal uint Flags;
    }

    [LibraryImport("user32.dll", EntryPoint = "FindWindowExW", StringMarshalling = StringMarshalling.Utf16)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    private static partial nint FindWindowExW(nint parent, nint after, string? className, string windowName);
    [LibraryImport("user32.dll", EntryPoint = "GetWindowThreadProcessId")]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    private static partial uint GetWindowThreadProcessId(nint window, out uint processId);
    [LibraryImport("user32.dll", EntryPoint = "IsWindowVisible")]
    [return: MarshalAs(UnmanagedType.Bool)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    private static partial bool IsWindowVisible(nint window);
    [LibraryImport("user32.dll", EntryPoint = "IsIconic")]
    [return: MarshalAs(UnmanagedType.Bool)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    private static partial bool IsIconic(nint window);
    [LibraryImport("user32.dll", EntryPoint = "IsZoomed")]
    [return: MarshalAs(UnmanagedType.Bool)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    private static partial bool IsZoomed(nint window);
    [LibraryImport("user32.dll", EntryPoint = "GetWindow")]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    private static partial nint GetWindow(nint window, uint command);
    [LibraryImport("user32.dll", EntryPoint = "GetClientRect")]
    [return: MarshalAs(UnmanagedType.Bool)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    private static partial bool GetClientRect(nint window, out Rect rect);
    [LibraryImport("user32.dll", EntryPoint = "ClientToScreen")]
    [return: MarshalAs(UnmanagedType.Bool)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    private static partial bool ClientToScreen(nint window, ref Point point);
    [LibraryImport("user32.dll", EntryPoint = "MonitorFromWindow")]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    private static partial nint MonitorFromWindow(nint window, uint flags);
    [LibraryImport("user32.dll", EntryPoint = "MonitorFromRect")]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    private static partial nint MonitorFromRect(in Rect rect, uint flags);
    [LibraryImport("user32.dll", EntryPoint = "GetMonitorInfoW")]
    [return: MarshalAs(UnmanagedType.Bool)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    private static partial bool GetMonitorInfoW(nint monitor, ref MonitorInfo info);
    [LibraryImport("user32.dll", EntryPoint = "GetDpiForSystem")]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    private static partial uint GetDpiForSystem();
    [LibraryImport("user32.dll", EntryPoint = "GetDpiForWindow")]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    private static partial uint GetDpiForWindow(nint window);
}
