using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace ThisIsMyPC.Interop.Win32.Hardware.Hid;

/// <summary>setupapi.dll, hid.dll and kernel32.dll entry points behind <see cref="WindowsHidTransport"/>.</summary>
internal static partial class NativeHid
{
    internal const uint DIGCF_PRESENT = 0x00000002;
    internal const uint DIGCF_DEVICEINTERFACE = 0x00000010;
    internal const uint GENERIC_READ = 0x80000000;
    internal const uint GENERIC_WRITE = 0x40000000;
    internal const uint FILE_SHARE_READ = 0x00000001;
    internal const uint FILE_SHARE_WRITE = 0x00000002;
    internal const uint OPEN_EXISTING = 3;
    internal const uint FILE_FLAG_OVERLAPPED = 0x40000000;
    internal const int ERROR_IO_PENDING = 997;
    internal const int ERROR_INSUFFICIENT_BUFFER = 122;
    internal const int ERROR_NO_MORE_ITEMS = 259;
    internal const uint WAIT_OBJECT_0 = 0;

    [StructLayout(LayoutKind.Sequential)]
    internal struct SP_DEVICE_INTERFACE_DATA
    {
        public uint cbSize;
        public Guid InterfaceClassGuid;
        public uint Flags;
        public nuint Reserved;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct HIDD_ATTRIBUTES
    {
        public uint Size;
        public ushort VendorID;
        public ushort ProductID;
        public ushort VersionNumber;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal unsafe struct HIDP_CAPS
    {
        public ushort Usage;
        public ushort UsagePage;
        public ushort InputReportByteLength;
        public ushort OutputReportByteLength;
        public ushort FeatureReportByteLength;
        public fixed ushort Reserved[17];
        public ushort NumberLinkCollectionNodes;
        public ushort NumberInputButtonCaps;
        public ushort NumberInputValueCaps;
        public ushort NumberInputDataIndices;
        public ushort NumberOutputButtonCaps;
        public ushort NumberOutputValueCaps;
        public ushort NumberOutputDataIndices;
        public ushort NumberFeatureButtonCaps;
        public ushort NumberFeatureValueCaps;
        public ushort NumberFeatureDataIndices;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct OVERLAPPED
    {
        public nuint Internal;
        public nuint InternalHigh;
        public uint Offset;
        public uint OffsetHigh;
        public nint hEvent;
    }

    [LibraryImport("hid.dll")]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    internal static partial void HidD_GetHidGuid(out Guid hidGuid);

    [LibraryImport("setupapi.dll", SetLastError = true, StringMarshalling = StringMarshalling.Utf16)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    internal static partial nint SetupDiGetClassDevsW(in Guid classGuid, string? enumerator, nint parent, uint flags);

    [LibraryImport("setupapi.dll", SetLastError = true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool SetupDiEnumDeviceInterfaces(nint deviceInfoSet, nint deviceInfoData, in Guid interfaceClassGuid, uint memberIndex, ref SP_DEVICE_INTERFACE_DATA deviceInterfaceData);

    [LibraryImport("setupapi.dll", SetLastError = true, EntryPoint = "SetupDiGetDeviceInterfaceDetailW")]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool SetupDiGetDeviceInterfaceDetailW(nint deviceInfoSet, ref SP_DEVICE_INTERFACE_DATA deviceInterfaceData, nint detailData, uint detailDataSize, out uint requiredSize, nint deviceInfoData);

    [LibraryImport("setupapi.dll", SetLastError = true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool SetupDiDestroyDeviceInfoList(nint deviceInfoSet);

    [LibraryImport("kernel32.dll", SetLastError = true, StringMarshalling = StringMarshalling.Utf16)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    internal static partial SafeFileHandle CreateFileW(string fileName, uint desiredAccess, uint shareMode, nint securityAttributes, uint creationDisposition, uint flagsAndAttributes, nint templateFile);

    [LibraryImport("hid.dll", SetLastError = true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [return: MarshalAs(UnmanagedType.U1)]
    internal static partial bool HidD_GetAttributes(SafeFileHandle device, ref HIDD_ATTRIBUTES attributes);

    [LibraryImport("hid.dll", SetLastError = true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [return: MarshalAs(UnmanagedType.U1)]
    internal static partial bool HidD_GetPreparsedData(SafeFileHandle device, out nint preparsedData);

    [LibraryImport("hid.dll", SetLastError = true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [return: MarshalAs(UnmanagedType.U1)]
    internal static partial bool HidD_FreePreparsedData(nint preparsedData);

    [LibraryImport("hid.dll", SetLastError = true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    internal static partial int HidP_GetCaps(nint preparsedData, out HIDP_CAPS capabilities);

    [LibraryImport("hid.dll", SetLastError = true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [return: MarshalAs(UnmanagedType.U1)]
    internal static partial bool HidD_GetManufacturerString(SafeFileHandle device, ref ushort buffer, uint bufferLength);

    [LibraryImport("hid.dll", SetLastError = true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [return: MarshalAs(UnmanagedType.U1)]
    internal static partial bool HidD_GetProductString(SafeFileHandle device, ref ushort buffer, uint bufferLength);

    [LibraryImport("hid.dll", SetLastError = true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [return: MarshalAs(UnmanagedType.U1)]
    internal static partial bool HidD_GetSerialNumberString(SafeFileHandle device, ref ushort buffer, uint bufferLength);

    [LibraryImport("hid.dll", SetLastError = true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [return: MarshalAs(UnmanagedType.U1)]
    internal static partial bool HidD_SetFeature(SafeFileHandle device, in byte reportBuffer, uint reportBufferLength);

    [LibraryImport("hid.dll", SetLastError = true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [return: MarshalAs(UnmanagedType.U1)]
    internal static partial bool HidD_GetFeature(SafeFileHandle device, ref byte reportBuffer, uint reportBufferLength);

    [LibraryImport("kernel32.dll", SetLastError = true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool WriteFile(SafeFileHandle file, in byte buffer, uint bytesToWrite, out uint bytesWritten, ref OVERLAPPED overlapped);

    [LibraryImport("kernel32.dll", SetLastError = true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool ReadFile(SafeFileHandle file, ref byte buffer, uint bytesToRead, out uint bytesRead, ref OVERLAPPED overlapped);

    [LibraryImport("kernel32.dll", SetLastError = true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool GetOverlappedResult(SafeFileHandle file, ref OVERLAPPED overlapped, out uint bytesTransferred, [MarshalAs(UnmanagedType.Bool)] bool wait);

    [LibraryImport("kernel32.dll", SetLastError = true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool CancelIo(SafeFileHandle file);

    [LibraryImport("kernel32.dll", SetLastError = true, StringMarshalling = StringMarshalling.Utf16)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    internal static partial nint CreateEventW(nint eventAttributes, [MarshalAs(UnmanagedType.Bool)] bool manualReset, [MarshalAs(UnmanagedType.Bool)] bool initialState, string? name);

    [LibraryImport("kernel32.dll", SetLastError = true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    internal static partial uint WaitForSingleObject(nint handle, uint milliseconds);

    [LibraryImport("kernel32.dll", SetLastError = true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool CloseHandle(nint handle);
}
