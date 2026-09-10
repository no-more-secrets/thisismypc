using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace ThisIsMyPC.Installer.Win32;

internal static unsafe partial class NativeMethods
{
    internal const uint WM_NCCREATE = 0x0081;
    internal const uint WM_CREATE = 0x0001;
    internal const uint WM_DESTROY = 0x0002;
    internal const uint WM_CLOSE = 0x0010;
    internal const uint WM_PAINT = 0x000F;
    internal const uint WM_COMMAND = 0x0111;
    internal const uint WM_TIMER = 0x0113;
    internal const uint WM_KEYDOWN = 0x0100;
    internal const uint WM_LBUTTONUP = 0x0202;
    internal const uint WM_MOUSEMOVE = 0x0200;
    internal const uint WM_MOUSELEAVE = 0x02A3;
    internal const uint WM_DPICHANGED = 0x02E0;
    internal const uint WM_CTLCOLOREDIT = 0x0133;
    internal const uint WM_CTLCOLORSTATIC = 0x0138;
    internal const uint WM_SETFONT = 0x0030;
    internal const uint WM_SETTEXT = 0x000C;
    internal const uint WM_APP_CALLBACK = 0x8001;

    internal const uint WS_OVERLAPPED = 0x00000000;
    internal const uint WS_CAPTION = 0x00C00000;
    internal const uint WS_SYSMENU = 0x00080000;
    internal const uint WS_MINIMIZEBOX = 0x00020000;
    internal const uint WS_CLIPCHILDREN = 0x02000000;
    internal const uint WS_CHILD = 0x40000000;
    internal const uint WS_VSCROLL = 0x00200000;
    internal const uint WS_HSCROLL = 0x00100000;
    internal const uint WS_TABSTOP = 0x00010000;
    internal const uint WS_EX_CLIENTEDGE = 0x00000200;

    internal const uint ES_LEFT = 0x0000;
    internal const uint ES_MULTILINE = 0x0004;
    internal const uint ES_AUTOVSCROLL = 0x0040;
    internal const uint ES_AUTOHSCROLL = 0x0080;
    internal const uint ES_READONLY = 0x0800;
    internal const uint EN_CHANGE = 0x0300;
    internal const uint EM_SETMARGINS = 0x00D3;
    internal const nuint EC_LEFTMARGIN = 0x0001;
    internal const nuint EC_RIGHTMARGIN = 0x0002;

    internal const int GWLP_USERDATA = -21;
    internal const int SW_HIDE = 0;
    internal const int SW_SHOWNORMAL = 1;
    internal const int SW_SHOWNA = 8;
    internal const int IDC_ARROW = 32512;
    internal const int IDI_APPLICATION = 32512;
    internal const int VK_RETURN = 0x0D;
    internal const int VK_ESCAPE = 0x1B;
    internal static readonly nint DPI_AWARENESS_CONTEXT_PER_MONITOR_AWARE_V2 = new(-4);
    internal const uint SWP_NOZORDER = 0x0004;
    internal const uint SWP_NOACTIVATE = 0x0010;
    internal const int TRANSPARENT = 1;
    internal const int PS_SOLID = 0;
    internal const int FW_NORMAL = 400;
    internal const int FW_SEMIBOLD = 600;
    internal const int FW_BOLD = 700;
    internal const uint DEFAULT_CHARSET = 1;
    internal const uint CLEARTYPE_QUALITY = 5;
    internal const uint DT_LEFT = 0x0000;
    internal const uint DT_CENTER = 0x0001;
    internal const uint DT_RIGHT = 0x0002;
    internal const uint DT_VCENTER = 0x0004;
    internal const uint DT_WORDBREAK = 0x0010;
    internal const uint DT_SINGLELINE = 0x0020;
    internal const uint DT_NOPREFIX = 0x0800;
    internal const uint DT_END_ELLIPSIS = 0x8000;
    internal const uint TME_LEAVE = 0x00000002;
    internal const uint BI_RGB = 0;
    internal const uint DIB_RGB_COLORS = 0;
    internal const uint BFFM_INITIALIZED = 1;
    internal const uint BFFM_SETSELECTIONW = 0x467;
    internal const uint BIF_RETURNONLYFSDIRS = 0x0001;
    internal const uint BIF_NEWDIALOGSTYLE = 0x0040;
    internal const uint BIF_EDITBOX = 0x0010;
    internal const uint BIF_USENEWUI = BIF_NEWDIALOGSTYLE | BIF_EDITBOX;

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    internal struct WNDCLASSEXW
    {
        internal uint cbSize;
        internal uint style;
        internal nint lpfnWndProc;
        internal int cbClsExtra;
        internal int cbWndExtra;
        internal nint hInstance;
        internal nint hIcon;
        internal nint hCursor;
        internal nint hbrBackground;
        internal nint lpszMenuName;
        internal nint lpszClassName;
        internal nint hIconSm;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct POINT
    {
        internal int X;
        internal int Y;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct MSG
    {
        internal nint hwnd;
        internal uint message;
        internal nuint wParam;
        internal nint lParam;
        internal uint time;
        internal POINT pt;
        internal uint lPrivate;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct RECT
    {
        internal int Left;
        internal int Top;
        internal int Right;
        internal int Bottom;

        internal RECT(int left, int top, int right, int bottom)
        {
            Left = left;
            Top = top;
            Right = right;
            Bottom = bottom;
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct CREATESTRUCTW
    {
        internal nint lpCreateParams;
        internal nint hInstance;
        internal nint hMenu;
        internal nint hwndParent;
        internal int cy;
        internal int cx;
        internal int y;
        internal int x;
        internal int style;
        internal nint lpszName;
        internal nint lpszClass;
        internal uint dwExStyle;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct PAINTSTRUCT
    {
        internal nint hdc;
        internal int fErase;
        internal RECT rcPaint;
        internal int fRestore;
        internal int fIncUpdate;
        internal fixed byte rgbReserved[32];
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct TRACKMOUSEEVENT
    {
        internal uint cbSize;
        internal uint dwFlags;
        internal nint hwndTrack;
        internal uint dwHoverTime;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct BITMAPINFOHEADER
    {
        internal uint biSize;
        internal int biWidth;
        internal int biHeight;
        internal ushort biPlanes;
        internal ushort biBitCount;
        internal uint biCompression;
        internal uint biSizeImage;
        internal int biXPelsPerMeter;
        internal int biYPelsPerMeter;
        internal uint biClrUsed;
        internal uint biClrImportant;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct BITMAPINFO
    {
        internal BITMAPINFOHEADER bmiHeader;
        internal uint bmiColors;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct BROWSEINFOW
    {
        internal nint hwndOwner;
        internal nint pidlRoot;
        internal nint pszDisplayName;
        internal nint lpszTitle;
        internal uint ulFlags;
        internal nint lpfn;
        internal nint lParam;
        internal int iImage;
    }

    [LibraryImport("kernel32.dll", EntryPoint = "GetModuleHandleW", StringMarshalling = StringMarshalling.Utf16)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    internal static partial nint GetModuleHandle(string? moduleName);

    [LibraryImport("user32.dll", EntryPoint = "RegisterClassExW", SetLastError = true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    internal static partial ushort RegisterClassEx(ref WNDCLASSEXW windowClass);

    [LibraryImport("user32.dll", EntryPoint = "CreateWindowExW", StringMarshalling = StringMarshalling.Utf16, SetLastError = true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    internal static partial nint CreateWindowEx(uint exStyle, string className, string windowName, uint style,
        int x, int y, int width, int height, nint parent, nint menu, nint instance, nint parameter);

    [LibraryImport("user32.dll", EntryPoint = "DefWindowProcW")]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    internal static partial nint DefWindowProc(nint hwnd, uint message, nuint wParam, nint lParam);

    [LibraryImport("user32.dll", EntryPoint = "DestroyWindow")]
    [return: MarshalAs(UnmanagedType.Bool)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    internal static partial bool DestroyWindow(nint hwnd);

    [LibraryImport("user32.dll", EntryPoint = "ShowWindow")]
    [return: MarshalAs(UnmanagedType.Bool)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    internal static partial bool ShowWindow(nint hwnd, int command);

    [LibraryImport("user32.dll", EntryPoint = "UpdateWindow")]
    [return: MarshalAs(UnmanagedType.Bool)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    internal static partial bool UpdateWindow(nint hwnd);

    [LibraryImport("user32.dll", EntryPoint = "GetMessageW", SetLastError = true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    internal static partial int GetMessage(out MSG message, nint hwnd, uint minimum, uint maximum);

    [LibraryImport("user32.dll", EntryPoint = "TranslateMessage")]
    [return: MarshalAs(UnmanagedType.Bool)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    internal static partial bool TranslateMessage(in MSG message);

    [LibraryImport("user32.dll", EntryPoint = "DispatchMessageW")]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    internal static partial nint DispatchMessage(in MSG message);

    [LibraryImport("user32.dll", EntryPoint = "PostQuitMessage")]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    internal static partial void PostQuitMessage(int exitCode);

    [LibraryImport("user32.dll", EntryPoint = "PostMessageW", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    internal static partial bool PostMessage(nint hwnd, uint message, nuint wParam, nint lParam);

    [LibraryImport("user32.dll", EntryPoint = "SetWindowLongPtrW", SetLastError = true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    internal static partial nint SetWindowLongPtr(nint hwnd, int index, nint value);

    [LibraryImport("user32.dll", EntryPoint = "GetWindowLongPtrW", SetLastError = true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    internal static partial nint GetWindowLongPtr(nint hwnd, int index);

    [LibraryImport("user32.dll", EntryPoint = "BeginPaint")]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    internal static partial nint BeginPaint(nint hwnd, out PAINTSTRUCT paint);

    [LibraryImport("user32.dll", EntryPoint = "EndPaint")]
    [return: MarshalAs(UnmanagedType.Bool)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    internal static partial bool EndPaint(nint hwnd, in PAINTSTRUCT paint);

    [LibraryImport("user32.dll", EntryPoint = "InvalidateRect")]
    [return: MarshalAs(UnmanagedType.Bool)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    internal static partial bool InvalidateRect(nint hwnd, nint rectangle, [MarshalAs(UnmanagedType.Bool)] bool erase);

    [LibraryImport("user32.dll", EntryPoint = "GetClientRect")]
    [return: MarshalAs(UnmanagedType.Bool)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    internal static partial bool GetClientRect(nint hwnd, out RECT rectangle);

    [LibraryImport("user32.dll", EntryPoint = "MoveWindow")]
    [return: MarshalAs(UnmanagedType.Bool)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    internal static partial bool MoveWindow(nint hwnd, int x, int y, int width, int height, [MarshalAs(UnmanagedType.Bool)] bool repaint);

    [LibraryImport("user32.dll", EntryPoint = "SetWindowPos")]
    [return: MarshalAs(UnmanagedType.Bool)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    internal static partial bool SetWindowPos(nint hwnd, nint insertAfter, int x, int y, int width, int height, uint flags);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    internal static partial bool SetProcessDpiAwarenessContext(nint value);

    [LibraryImport("user32.dll", EntryPoint = "EnableWindow")]
    [return: MarshalAs(UnmanagedType.Bool)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    internal static partial bool EnableWindow(nint hwnd, [MarshalAs(UnmanagedType.Bool)] bool enable);

    [LibraryImport("user32.dll", EntryPoint = "SendMessageW")]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    internal static partial nint SendMessage(nint hwnd, uint message, nuint wParam, nint lParam);

    [LibraryImport("user32.dll", EntryPoint = "SetWindowTextW", StringMarshalling = StringMarshalling.Utf16)]
    [return: MarshalAs(UnmanagedType.Bool)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    internal static partial bool SetWindowText(nint hwnd, string text);

    [LibraryImport("user32.dll", EntryPoint = "GetWindowTextLengthW")]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    internal static partial int GetWindowTextLength(nint hwnd);

    [LibraryImport("user32.dll", EntryPoint = "GetWindowTextW", StringMarshalling = StringMarshalling.Utf16)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    internal static partial int GetWindowText(nint hwnd, char[] text, int maximumCount);

    [LibraryImport("user32.dll", EntryPoint = "LoadCursorW")]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    internal static partial nint LoadCursor(nint instance, nint cursorName);

    [LibraryImport("user32.dll", EntryPoint = "LoadIconW")]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    internal static partial nint LoadIcon(nint instance, nint iconName);

    [LibraryImport("user32.dll", EntryPoint = "AdjustWindowRectExForDpi")]
    [return: MarshalAs(UnmanagedType.Bool)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    internal static partial bool AdjustWindowRectExForDpi(ref RECT rectangle, uint style, [MarshalAs(UnmanagedType.Bool)] bool hasMenu, uint exStyle, uint dpi);

    [LibraryImport("user32.dll", EntryPoint = "GetDpiForSystem")]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    internal static partial uint GetDpiForSystem();

    [LibraryImport("user32.dll", EntryPoint = "GetDpiForWindow")]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    internal static partial uint GetDpiForWindow(nint hwnd);

    [LibraryImport("user32.dll", EntryPoint = "GetSystemMetrics")]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    internal static partial int GetSystemMetrics(int index);

    [LibraryImport("user32.dll", EntryPoint = "TrackMouseEvent")]
    [return: MarshalAs(UnmanagedType.Bool)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    internal static partial bool TrackMouseEvent(ref TRACKMOUSEEVENT trackEvent);

    [LibraryImport("user32.dll", EntryPoint = "SetTimer")]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    internal static partial nuint SetTimer(nint hwnd, nuint id, uint milliseconds, nint callback);

    [LibraryImport("user32.dll", EntryPoint = "KillTimer")]
    [return: MarshalAs(UnmanagedType.Bool)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    internal static partial bool KillTimer(nint hwnd, nuint id);

    [LibraryImport("user32.dll", EntryPoint = "MessageBoxW", StringMarshalling = StringMarshalling.Utf16)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    internal static partial int MessageBox(nint owner, string text, string caption, uint type);

    [LibraryImport("gdi32.dll", EntryPoint = "CreateSolidBrush")]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    internal static partial nint CreateSolidBrush(uint color);

    [LibraryImport("gdi32.dll", EntryPoint = "CreatePen")]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    internal static partial nint CreatePen(int style, int width, uint color);

    [LibraryImport("gdi32.dll", EntryPoint = "CreateFontW", StringMarshalling = StringMarshalling.Utf16)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    internal static partial nint CreateFont(int height, int width, int escapement, int orientation, int weight,
        uint italic, uint underline, uint strikeOut, uint charSet, uint outputPrecision, uint clipPrecision,
        uint quality, uint pitchAndFamily, string faceName);

    [LibraryImport("gdi32.dll", EntryPoint = "SelectObject")]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    internal static partial nint SelectObject(nint hdc, nint value);

    [LibraryImport("gdi32.dll", EntryPoint = "DeleteObject")]
    [return: MarshalAs(UnmanagedType.Bool)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    internal static partial bool DeleteObject(nint value);

    [LibraryImport("gdi32.dll", EntryPoint = "SetTextColor")]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    internal static partial uint SetTextColor(nint hdc, uint color);

    [LibraryImport("gdi32.dll", EntryPoint = "SetBkColor")]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    internal static partial uint SetBkColor(nint hdc, uint color);

    [LibraryImport("gdi32.dll", EntryPoint = "SetBkMode")]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    internal static partial int SetBkMode(nint hdc, int mode);

    [LibraryImport("user32.dll", EntryPoint = "FillRect")]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    internal static partial int FillRect(nint hdc, in RECT rectangle, nint brush);

    [LibraryImport("gdi32.dll", EntryPoint = "RoundRect")]
    [return: MarshalAs(UnmanagedType.Bool)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    internal static partial bool RoundRect(nint hdc, int left, int top, int right, int bottom, int width, int height);

    [LibraryImport("gdi32.dll", EntryPoint = "Ellipse")]
    [return: MarshalAs(UnmanagedType.Bool)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    internal static partial bool Ellipse(nint hdc, int left, int top, int right, int bottom);

    [LibraryImport("gdi32.dll", EntryPoint = "MoveToEx")]
    [return: MarshalAs(UnmanagedType.Bool)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    internal static partial bool MoveToEx(nint hdc, int x, int y, nint oldPoint);

    [LibraryImport("gdi32.dll", EntryPoint = "LineTo")]
    [return: MarshalAs(UnmanagedType.Bool)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    internal static partial bool LineTo(nint hdc, int x, int y);

    [LibraryImport("user32.dll", EntryPoint = "DrawTextW", StringMarshalling = StringMarshalling.Utf16)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    internal static partial int DrawText(nint hdc, string text, int length, ref RECT rectangle, uint format);

    [LibraryImport("gdi32.dll", EntryPoint = "CreateCompatibleDC")]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    internal static partial nint CreateCompatibleDC(nint hdc);

    [LibraryImport("gdi32.dll", EntryPoint = "DeleteDC")]
    [return: MarshalAs(UnmanagedType.Bool)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    internal static partial bool DeleteDC(nint hdc);

    [LibraryImport("gdi32.dll", EntryPoint = "CreateDIBSection")]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    internal static partial nint CreateDIBSection(nint hdc, in BITMAPINFO info, uint usage, out nint bits, nint section, uint offset);

    [LibraryImport("shell32.dll", EntryPoint = "SHBrowseForFolderW")]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    internal static partial nint SHBrowseForFolder(ref BROWSEINFOW info);

    [LibraryImport("shell32.dll", EntryPoint = "SHGetPathFromIDListW")]
    [return: MarshalAs(UnmanagedType.Bool)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    internal static partial bool SHGetPathFromIDList(nint itemIdList, char* path);

    [LibraryImport("ole32.dll", EntryPoint = "CoTaskMemFree")]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    internal static partial void CoTaskMemFree(nint memory);

    [LibraryImport("ole32.dll", EntryPoint = "OleInitialize")]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    internal static partial int OleInitialize(nint reserved);

    [LibraryImport("ole32.dll", EntryPoint = "OleUninitialize")]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    internal static partial void OleUninitialize();

    [LibraryImport("dwmapi.dll", EntryPoint = "DwmSetWindowAttribute")]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    internal static partial int DwmSetWindowAttribute(nint hwnd, uint attribute, in int value, uint size);

    [LibraryImport("uxtheme.dll", EntryPoint = "SetWindowTheme", StringMarshalling = StringMarshalling.Utf16)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    internal static partial int SetWindowTheme(nint hwnd, string? subAppName, string? subIdList);
}
