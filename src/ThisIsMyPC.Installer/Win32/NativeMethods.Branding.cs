using System.Runtime.InteropServices;

namespace ThisIsMyPC.Installer.Win32;

internal static unsafe partial class NativeMethods
{
    internal const uint LOAD_LIBRARY_AS_DATAFILE = 0x2;
    internal const uint LOAD_LIBRARY_AS_IMAGE_RESOURCE = 0x20;
    internal const uint IMAGE_ICON = 1;
    internal const uint DI_NORMAL = 3;
    internal const int HALFTONE = 4;
    internal const uint SRCCOPY = 0x00CC0020;

    [LibraryImport("kernel32.dll", EntryPoint = "LoadLibraryExW", StringMarshalling = StringMarshalling.Utf16, SetLastError = true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    internal static partial nint LoadLibraryEx(string path, nint reserved, uint flags);

    [LibraryImport("comctl32.dll", EntryPoint = "LoadIconWithScaleDown")]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    internal static partial int LoadIconWithScaleDown(nint instance, nint name, int width, int height, out nint icon);

    [LibraryImport("user32.dll", EntryPoint = "LoadImageW")]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    internal static partial nint LoadImage(nint instance, nint name, uint type, int width, int height, uint flags);

    [LibraryImport("user32.dll", EntryPoint = "DrawIconEx")]
    [return: MarshalAs(UnmanagedType.Bool)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    internal static partial bool DrawIconEx(nint dc, int x, int y, nint icon, int width, int height, uint step, nint brush, uint flags);

    [LibraryImport("gdi32.dll", EntryPoint = "SetStretchBltMode")]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    internal static partial int SetStretchBltMode(nint dc, int mode);

    [LibraryImport("gdi32.dll", EntryPoint = "SetBrushOrgEx")]
    [return: MarshalAs(UnmanagedType.Bool)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    internal static partial bool SetBrushOrgEx(nint dc, int x, int y, nint previous);

    [LibraryImport("gdi32.dll", EntryPoint = "StretchDIBits")]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    internal static partial int StretchDIBits(nint dc, int x, int y, int width, int height,
        int sourceX, int sourceY, int sourceWidth, int sourceHeight, byte* bits, in BITMAPINFO info, uint usage, uint rop);
}
