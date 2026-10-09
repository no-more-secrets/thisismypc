using System.Runtime.InteropServices;

namespace ThisIsMyPC.Broker;

// The broker locates the verified UI process itself. No window handle or position comes from IPC.
internal static partial class BrokerReviewPlacement
{
    private const string MainWindowTitle = "ThisIsMyPC";
    private const int ApplyRightInset = 41;
    private const int ReviewButtonRightInset = 16;
    private const int ApplyBarTopInset = 82;

    internal readonly record struct Position(int X, int Y);

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
        // The bar starts 74 pixels above the client bottom; the original popup has an 8 pixel gap.
        // Cross-process window coordinates use the calling broker thread's DPI space.
        var dpi = GetDpiForSystem();
        var scale = (dpi == 0 ? 1.0 : Math.Clamp(dpi / 96.0, 0.75, 4.0))
            * BrokerReviewZoom.ForUiSid(uiSid);
        var appRight = checked(clientOrigin.X + client.Right - client.Left);
        var appBottom = checked(clientOrigin.Y + client.Bottom - client.Top);
        if (IsZoomed(uiWindow))
        {
            // Avalonia's OffScreenMargin removes the maximized frame outside the work area.
            appRight = Math.Min(appRight, monitorInfo.Work.Right);
            appBottom = Math.Min(appBottom, monitorInfo.Work.Bottom);
        }
        return Align(appRight, appBottom, scale, width, height,
            monitorInfo.Work.Left, monitorInfo.Work.Top, monitorInfo.Work.Right, monitorInfo.Work.Bottom);
    }

    internal static Position Align(int appRight, int appBottom, double scale, int width, int height,
        int workLeft, int workTop, int workRight, int workBottom)
    {
        var x = checked(appRight - (int)Math.Round(ApplyRightInset * scale) + ReviewButtonRightInset - width);
        var y = checked(appBottom - (int)Math.Round(ApplyBarTopInset * scale) - height);
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
    [LibraryImport("user32.dll", EntryPoint = "GetMonitorInfoW")]
    [return: MarshalAs(UnmanagedType.Bool)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    private static partial bool GetMonitorInfoW(nint monitor, ref MonitorInfo info);
    [LibraryImport("user32.dll", EntryPoint = "GetDpiForSystem")]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    private static partial uint GetDpiForSystem();
}
