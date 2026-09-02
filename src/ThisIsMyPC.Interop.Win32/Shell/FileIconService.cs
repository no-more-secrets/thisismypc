using System.Runtime.InteropServices;
using ThisIsMyPC.Core.Results;
using ThisIsMyPC.Core.Services;

namespace ThisIsMyPC.Interop.Win32.Shell;

/// <summary>
/// The shell's small icon for a file, read into BGRA pixels with GDI:
/// SHGetFileInfoW for the HICON, GetIconInfo for its color and mask
/// bitmaps, GetDIBits for the bytes. A missing file gets the icon for its
/// extension (SHGFI_USEFILEATTRIBUTES), the way Explorer shows a dead link.
/// </summary>
public sealed partial class FileIconService : IFileIconService
{
    private const uint SHGFI_ICON = 0x000000100;
    private const uint SHGFI_SMALLICON = 0x000000001;
    private const uint SHGFI_USEFILEATTRIBUTES = 0x000000010;
    private const uint FILE_ATTRIBUTE_NORMAL = 0x00000080;
    private const uint DIB_RGB_COLORS = 0;
    private const uint BI_RGB = 0;

    public OperationResult<FileIcon> GetSmallIcon(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        nint hIcon = 0;
        try
        {
            var flags = SHGFI_ICON | SHGFI_SMALLICON;
            if (!File.Exists(path))
                flags |= SHGFI_USEFILEATTRIBUTES;

            var info = new SHFILEINFOW();
            var got = SHGetFileInfoW(path, FILE_ATTRIBUTE_NORMAL, ref info, (uint)Marshal.SizeOf<SHFILEINFOW>(), flags);
            hIcon = info.hIcon;
            if (got == 0 || hIcon == 0)
                return OperationResult<FileIcon>.Failure($"No icon for {path}", ErrorCategory.NotFound);

            var icon = ReadIcon(hIcon);
            return icon is null
                ? OperationResult<FileIcon>.Failure($"Icon for {path} could not be read", ErrorCategory.ServiceUnavailable)
                : OperationResult<FileIcon>.Success(icon);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
        {
            return OperationResult<FileIcon>.Failure($"Icon for {path}: {ex.Message}", ErrorCategory.ServiceUnavailable, ex);
        }
        finally
        {
            if (hIcon != 0)
                DestroyIcon(hIcon);
        }
    }

    private static unsafe FileIcon? ReadIcon(nint hIcon)
    {
        if (!GetIconInfo(hIcon, out var iconInfo))
            return null;
        try
        {
            if (iconInfo.hbmColor == 0)
                return null; // monochrome icon; not worth a special path

            var bitmap = new BITMAP();
            if (GetObjectW(iconInfo.hbmColor, sizeof(BITMAP), &bitmap) == 0 || bitmap.bmWidth <= 0 || bitmap.bmHeight <= 0)
                return null;

            var width = bitmap.bmWidth;
            var height = bitmap.bmHeight;
            var color = ReadBits(iconInfo.hbmColor, width, height);
            if (color is null)
                return null;

            // Icons without an alpha channel carry transparency in the mask
            // (white = transparent). Only fill alpha in when the color bits have none.
            var hasAlpha = false;
            for (var i = 3; i < color.Length; i += 4)
            {
                if (color[i] != 0)
                {
                    hasAlpha = true;
                    break;
                }
            }
            if (!hasAlpha && iconInfo.hbmMask != 0)
            {
                var mask = ReadBits(iconInfo.hbmMask, width, height);
                for (var i = 0; i < color.Length; i += 4)
                    color[i + 3] = mask is not null && mask[i] != 0 ? (byte)0 : (byte)255;
            }
            else if (!hasAlpha)
            {
                for (var i = 3; i < color.Length; i += 4)
                    color[i] = 255;
            }

            return new FileIcon(width, height, color);
        }
        finally
        {
            if (iconInfo.hbmColor != 0)
                DeleteObject(iconInfo.hbmColor);
            if (iconInfo.hbmMask != 0)
                DeleteObject(iconInfo.hbmMask);
        }
    }

    /// <summary>32-bit top-down BGRA copy of a bitmap through a screen DC.</summary>
    private static unsafe byte[]? ReadBits(nint hBitmap, int width, int height)
    {
        var hdc = GetDC(0);
        if (hdc == 0)
            return null;
        try
        {
            var header = new BITMAPINFOHEADER
            {
                biSize = (uint)sizeof(BITMAPINFOHEADER),
                biWidth = width,
                biHeight = -height, // negative: top-down rows
                biPlanes = 1,
                biBitCount = 32,
                biCompression = BI_RGB,
            };
            var pixels = new byte[width * height * 4];
            fixed (byte* pixelPtr = pixels)
            {
                var lines = GetDIBits(hdc, hBitmap, 0, (uint)height, pixelPtr, &header, DIB_RGB_COLORS);
                return lines == height ? pixels : null;
            }
        }
        finally
        {
            _ = ReleaseDC(0, hdc);
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    private unsafe struct SHFILEINFOW
    {
        public nint hIcon;
        public int iIcon;
        public uint dwAttributes;
        public fixed char szDisplayName[260];
        public fixed char szTypeName[80];
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct ICONINFO
    {
        public int fIcon;
        public int xHotspot;
        public int yHotspot;
        public nint hbmMask;
        public nint hbmColor;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct BITMAP
    {
        public int bmType;
        public int bmWidth;
        public int bmHeight;
        public int bmWidthBytes;
        public ushort bmPlanes;
        public ushort bmBitsPixel;
        public nint bmBits;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct BITMAPINFOHEADER
    {
        public uint biSize;
        public int biWidth;
        public int biHeight;
        public ushort biPlanes;
        public ushort biBitCount;
        public uint biCompression;
        public uint biSizeImage;
        public int biXPelsPerMeter;
        public int biYPelsPerMeter;
        public uint biClrUsed;
        public uint biClrImportant;
    }

    [LibraryImport("shell32.dll", StringMarshalling = StringMarshalling.Utf16)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    private static partial nint SHGetFileInfoW(string pszPath, uint dwFileAttributes, ref SHFILEINFOW psfi, uint cbFileInfo, uint uFlags);

    [LibraryImport("user32.dll")]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool GetIconInfo(nint hIcon, out ICONINFO piconinfo);

    [LibraryImport("user32.dll")]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool DestroyIcon(nint hIcon);

    [LibraryImport("user32.dll")]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    private static partial nint GetDC(nint hWnd);

    [LibraryImport("user32.dll")]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    private static partial int ReleaseDC(nint hWnd, nint hDC);

    [LibraryImport("gdi32.dll")]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    private static unsafe partial int GetObjectW(nint hObject, int cbBuffer, void* lpvObject);

    [LibraryImport("gdi32.dll")]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    private static unsafe partial int GetDIBits(nint hdc, nint hbm, uint start, uint cLines, byte* lpvBits, BITMAPINFOHEADER* lpbmi, uint usage);

    [LibraryImport("gdi32.dll")]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool DeleteObject(nint hObject);
}
