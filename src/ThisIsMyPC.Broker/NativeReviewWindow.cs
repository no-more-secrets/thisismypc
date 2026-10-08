using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace ThisIsMyPC.Broker;

// This window runs in the elevated broker. It uses only stock Win32 controls.
internal static unsafe partial class NativeReviewWindow
{
    internal enum Decision { Failed, Cancelled, Discarded, Apply }
    private const uint WsOverlappedWindow = 0x00CF0000;
    private const uint WsVisible = 0x10000000;
    private const uint WsChild = 0x40000000;
    private const uint WsTabStop = 0x00010000;
    private const uint WsVScroll = 0x00200000;
    private const uint WsExClientEdge = 0x00000200;
    private const uint EsMultiline = 0x0004;
    private const uint EsAutoVScroll = 0x0040;
    private const uint EsReadOnly = 0x0800;
    private const uint WmCommand = 0x0111;
    private const uint WmClose = 0x0010;
    private const uint WmDestroy = 0x0002;
    private const uint EmLimitText = 0x00C5;
    private const uint WmSetFont = 0x0030;
    private const int ApplyButton = 1001;
    private const int DiscardButton = 1002;
    private const string ClassName = "ThisIsMyPCBrokerReview";
    private static ReviewState? _current;

    internal static Decision Show(BrokerRequestPolicy policy)
    {
        var content = string.Join("\r\n\r\n", policy.BuildConfirmationPages());
        if (content.Length is 0 or > 1_000_000 || _current is not null)
            return Decision.Failed;

        var state = new ReviewState();
        _current = state;
        var className = Marshal.StringToHGlobalUni(ClassName);
        try
        {
            var windowClass = new WindowClass
            {
                Size = (uint)sizeof(WindowClass),
                WindowProc = (nint)(delegate* unmanaged[Stdcall]<nint, uint, nuint, nint, nint>)&WindowProc,
                Instance = GetModuleHandleW(null),
                Cursor = LoadCursorW(0, (nint)32512),
                Background = GetSysColorBrush(15),
                ClassName = className,
            };
            var atom = RegisterClassExW(ref windowClass);
            if (atom == 0)
                return Decision.Failed;
            try
            {
                var width = Math.Min(800, Math.Max(320, GetSystemMetrics(0) - 40));
                var height = Math.Min(620, Math.Max(300, GetSystemMetrics(1) - 40));
                var x = Math.Max(0, (GetSystemMetrics(0) - width) / 2);
                var y = Math.Max(0, (GetSystemMetrics(1) - height) / 2);
                var window = CreateWindowExW(0, ClassName, "ThisIsMyPC administrator review",
                    WsOverlappedWindow, x, y, width, height, 0, 0, windowClass.Instance, 0);
                if (window == 0)
                    return Decision.Failed;

                if (!GetClientRect(window, out var client))
                {
                    _ = DestroyWindow(window);
                    return Decision.Failed;
                }
                var clientWidth = client.Right - client.Left;
                var clientHeight = client.Bottom - client.Top;

                var introduction = CreateWindowExW(0, "STATIC",
                    "Review the exact changes below before applying them.",
                    WsChild | WsVisible, 20, 18, clientWidth - 40, 24, window, 0, windowClass.Instance, 0);
                var details = CreateWindowExW(WsExClientEdge, "EDIT", "",
                    WsChild | WsVisible | WsTabStop | WsVScroll | EsMultiline | EsAutoVScroll | EsReadOnly,
                    20, 50, clientWidth - 40, clientHeight - 116, window, 0, windowClass.Instance, 0);
                var discard = CreateWindowExW(0, "BUTTON", policy.HasBatchOperations ? "Discard All" : "Cancel",
                    WsChild | WsVisible | WsTabStop | 0x00000001,
                    clientWidth - 244, clientHeight - 50, 112, 32, window, (nint)DiscardButton, windowClass.Instance, 0);
                var apply = CreateWindowExW(0, "BUTTON", policy.HasBatchOperations ? "Apply All" : "Confirm",
                    WsChild | WsVisible | WsTabStop,
                    clientWidth - 128, clientHeight - 50, 112, 32, window, (nint)ApplyButton, windowClass.Instance, 0);
                if (introduction == 0 || details == 0 || discard == 0 || apply == 0)
                {
                    _ = DestroyWindow(window);
                    return Decision.Failed;
                }
                var font = GetStockObject(17);
                foreach (var control in new[] { introduction, details, discard, apply })
                    _ = SendMessageW(control, WmSetFont, (nuint)font, 1);
                _ = SendMessageW(details, EmLimitText, 1_000_000, 0);
                if (!SetWindowTextW(details, content) || GetWindowTextLengthW(details) != content.Length)
                {
                    _ = DestroyWindow(window);
                    return Decision.Failed;
                }
                _ = ShowWindow(window, 5);
                _ = UpdateWindow(window);
                _ = SetForegroundWindow(window);
                _ = SetFocus(discard);

                while (GetMessageW(out var message, 0, 0, 0) > 0)
                {
                    _ = TranslateMessage(in message);
                    _ = DispatchMessageW(in message);
                }
                return state.Result;
            }
            finally { _ = UnregisterClassW(ClassName, windowClass.Instance); }
        }
        finally
        {
            Marshal.FreeHGlobal(className);
            _current = null;
        }
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvStdcall)])]
    private static nint WindowProc(nint window, uint message, nuint wParam, nint lParam)
    {
        if (message == WmCommand && (wParam & 0xffff) is ApplyButton or DiscardButton)
        {
            _current!.Result = (wParam & 0xffff) == ApplyButton ? Decision.Apply : Decision.Discarded;
            _ = DestroyWindow(window);
            return 0;
        }
        if (message == WmClose)
        {
            _ = DestroyWindow(window);
            return 0;
        }
        if (message == WmDestroy)
        {
            PostQuitMessage(0);
            return 0;
        }
        return DefWindowProcW(window, message, wParam, lParam);
    }

    private sealed class ReviewState { internal Decision Result = Decision.Cancelled; }

    [StructLayout(LayoutKind.Sequential)]
    private struct WindowClass
    {
        internal uint Size;
        internal uint Style;
        internal nint WindowProc;
        internal int ClassExtra;
        internal int WindowExtra;
        internal nint Instance;
        internal nint Icon;
        internal nint Cursor;
        internal nint Background;
        internal nint MenuName;
        internal nint ClassName;
        internal nint SmallIcon;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct Point { internal int X; internal int Y; }

    [StructLayout(LayoutKind.Sequential)]
    private struct Rect { internal int Left; internal int Top; internal int Right; internal int Bottom; }

    [StructLayout(LayoutKind.Sequential)]
    private struct Message
    {
        internal nint Window;
        internal uint Id;
        internal nuint WParam;
        internal nint LParam;
        internal uint Time;
        internal Point Location;
        internal uint Private;
    }

    [LibraryImport("kernel32.dll", EntryPoint = "GetModuleHandleW", StringMarshalling = StringMarshalling.Utf16)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    private static partial nint GetModuleHandleW(string? moduleName);
    [LibraryImport("user32.dll", EntryPoint = "RegisterClassExW", SetLastError = true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    private static partial ushort RegisterClassExW(ref WindowClass windowClass);
    [LibraryImport("user32.dll", EntryPoint = "UnregisterClassW", StringMarshalling = StringMarshalling.Utf16)]
    [return: MarshalAs(UnmanagedType.Bool)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    private static partial bool UnregisterClassW(string className, nint instance);
    [LibraryImport("user32.dll", EntryPoint = "CreateWindowExW", StringMarshalling = StringMarshalling.Utf16, SetLastError = true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    private static partial nint CreateWindowExW(uint exStyle, string className, string title, uint style,
        int x, int y, int width, int height, nint parent, nint menu, nint instance, nint parameter);
    [LibraryImport("user32.dll", EntryPoint = "DefWindowProcW")]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    private static partial nint DefWindowProcW(nint window, uint message, nuint wParam, nint lParam);
    [LibraryImport("user32.dll", EntryPoint = "DestroyWindow")]
    [return: MarshalAs(UnmanagedType.Bool)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    private static partial bool DestroyWindow(nint window);
    [LibraryImport("user32.dll", EntryPoint = "GetMessageW")]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    private static partial int GetMessageW(out Message message, nint window, uint min, uint max);
    [LibraryImport("user32.dll", EntryPoint = "TranslateMessage")]
    [return: MarshalAs(UnmanagedType.Bool)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    private static partial bool TranslateMessage(in Message message);
    [LibraryImport("user32.dll", EntryPoint = "DispatchMessageW")]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    private static partial nint DispatchMessageW(in Message message);
    [LibraryImport("user32.dll", EntryPoint = "PostQuitMessage")]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    private static partial void PostQuitMessage(int exitCode);
    [LibraryImport("user32.dll", EntryPoint = "ShowWindow")]
    [return: MarshalAs(UnmanagedType.Bool)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    private static partial bool ShowWindow(nint window, int command);
    [LibraryImport("user32.dll", EntryPoint = "UpdateWindow")]
    [return: MarshalAs(UnmanagedType.Bool)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    private static partial bool UpdateWindow(nint window);
    [LibraryImport("user32.dll", EntryPoint = "SetFocus")]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    private static partial nint SetFocus(nint window);
    [LibraryImport("user32.dll", EntryPoint = "SetWindowTextW", StringMarshalling = StringMarshalling.Utf16)]
    [return: MarshalAs(UnmanagedType.Bool)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    private static partial bool SetWindowTextW(nint window, string text);
    [LibraryImport("user32.dll", EntryPoint = "GetWindowTextLengthW")]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    private static partial int GetWindowTextLengthW(nint window);
    [LibraryImport("user32.dll", EntryPoint = "SendMessageW")]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    private static partial nint SendMessageW(nint window, uint message, nuint wParam, nint lParam);
    [LibraryImport("user32.dll", EntryPoint = "GetSystemMetrics")]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    private static partial int GetSystemMetrics(int index);
    [LibraryImport("user32.dll", EntryPoint = "LoadCursorW")]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    private static partial nint LoadCursorW(nint instance, nint cursor);
    [LibraryImport("user32.dll", EntryPoint = "GetSysColorBrush")]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    private static partial nint GetSysColorBrush(int index);
    [LibraryImport("gdi32.dll", EntryPoint = "GetStockObject")]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    private static partial nint GetStockObject(int index);
    [LibraryImport("user32.dll", EntryPoint = "GetClientRect")]
    [return: MarshalAs(UnmanagedType.Bool)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    private static partial bool GetClientRect(nint window, out Rect rectangle);
    [LibraryImport("user32.dll", EntryPoint = "SetForegroundWindow")]
    [return: MarshalAs(UnmanagedType.Bool)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    private static partial bool SetForegroundWindow(nint window);
}
