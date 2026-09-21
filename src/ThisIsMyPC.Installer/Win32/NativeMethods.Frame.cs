using System.Runtime.InteropServices;

namespace ThisIsMyPC.Installer.Win32;

internal static unsafe partial class NativeMethods
{
    internal const uint WM_NCCALCSIZE = 0x0083;
    internal const uint WM_NCHITTEST = 0x0084;
    internal const uint WM_NCACTIVATE = 0x0086;
    internal const int HTCLIENT = 1;
    internal const int HTCAPTION = 2;
    internal const int SW_MINIMIZE = 6;
    internal const uint DWMWA_WINDOW_CORNER_PREFERENCE = 33;
    internal const int DWMWCP_ROUND = 2;

    [LibraryImport("gdi32.dll", EntryPoint = "CreateRectRgn")]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    internal static partial nint CreateRectRgn(int left, int top, int right, int bottom);

    /// <summary>The window owns the region afterwards; never delete it.</summary>
    [LibraryImport("user32.dll", EntryPoint = "SetWindowRgn")]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    internal static partial int SetWindowRgn(nint hwnd, nint region, [MarshalAs(UnmanagedType.Bool)] bool redraw);

    // The modern folder picker: IFileOpenDialog over raw vtables (NativeAOT-safe, no ComWrappers).
    internal const uint CLSCTX_INPROC_SERVER = 1;
    internal const uint COINIT_APARTMENTTHREADED = 0x2;
    internal const uint FOS_NOCHANGEDIR = 0x8;
    internal const uint FOS_PICKFOLDERS = 0x20;
    internal const uint FOS_FORCEFILESYSTEM = 0x40;
    internal const uint FOS_PATHMUSTEXIST = 0x800;
    internal const uint SIGDN_FILESYSPATH = 0x80058000;
    internal const int HRESULT_CANCELLED = unchecked((int)0x800704C7);
    internal static readonly Guid CLSID_FileOpenDialog = new("DC1C5A9C-E88A-4DDE-A5A1-60F82A20AEF7");
    internal static readonly Guid IID_IFileOpenDialog = new("D57C7288-D4AD-4768-BE02-9D969532D960");
    internal static readonly Guid IID_IShellItem = new("43826D1E-E718-42EE-BC55-A1E261C37BFE");

    [LibraryImport("ole32.dll", EntryPoint = "CoInitializeEx")]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    internal static partial int CoInitializeEx(nint reserved, uint model);

    [LibraryImport("ole32.dll", EntryPoint = "CoUninitialize")]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    internal static partial void CoUninitialize();

    [LibraryImport("ole32.dll", EntryPoint = "CoCreateInstance")]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    internal static partial int CoCreateInstance(in Guid clsid, nint outer, uint context, in Guid iid, out nint instance);

    [LibraryImport("ole32.dll", EntryPoint = "CoTaskMemFree")]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    internal static partial void CoTaskMemFree(nint memory);

    [LibraryImport("shell32.dll", EntryPoint = "SHCreateItemFromParsingName", StringMarshalling = StringMarshalling.Utf16)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    internal static partial int SHCreateItemFromParsingName(string path, nint bindContext, in Guid iid, out nint item);
}
