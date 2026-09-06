using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace ThisIsMyPC.Interop.Win32.Drift.Consent;

internal static partial class NativeConsentFiles
{
    internal const uint ReadControl = 0x20000;
    internal const uint ReadAttributes = 0x80;
    internal const uint GenericRead = 0x80000000;
    internal const uint GenericWrite = 0x40000000;
    internal const uint OpenReparsePoint = 0x00200000;
    internal const uint BackupSemantics = 0x02000000;
    internal const uint Directory = 0x10;
    internal const uint ReparsePoint = 0x400;

    [LibraryImport("kernel32.dll", StringMarshalling = StringMarshalling.Utf16)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    internal static partial uint GetDriveTypeW(string rootPath);
    [StructLayout(LayoutKind.Sequential)]
    internal struct FileInformation
    {
        public uint Attributes;
        public uint CreationTimeLow; public uint CreationTimeHigh;
        public uint LastAccessTimeLow; public uint LastAccessTimeHigh;
        public uint LastWriteTimeLow; public uint LastWriteTimeHigh;
        public uint VolumeSerialNumber;
        public uint FileSizeHigh;
        public uint FileSizeLow;
        public uint NumberOfLinks;
        public uint FileIndexHigh;
        public uint FileIndexLow;
    }

    [LibraryImport("kernel32.dll", SetLastError = true, StringMarshalling = StringMarshalling.Utf16)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    internal static partial SafeFileHandle CreateFileW(string path, uint access, uint sharing,
        nint securityAttributes, uint disposition, uint flags, nint template);

    [LibraryImport("kernel32.dll", SetLastError = true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool GetFileInformationByHandle(SafeFileHandle file, out FileInformation information);

    [LibraryImport("kernel32.dll", SetLastError = true, StringMarshalling = StringMarshalling.Utf16)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool MoveFileExW(string existing, string destination, uint flags);
}