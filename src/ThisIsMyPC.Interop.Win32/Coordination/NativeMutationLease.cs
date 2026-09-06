using System.Runtime.InteropServices;

namespace ThisIsMyPC.Interop.Win32.Coordination;

/// <summary>
/// Kernel mutex, event, and handle-security calls for the mutation lease. Every
/// call here runs on the lease's dedicated owner thread; nothing else in the
/// assembly touches these handles.
/// </summary>
internal static partial class NativeMutationLease
{
    internal const uint MutexAllAccess = 0x001F0001;
    internal const uint GenericAll = 0x10000000;

    internal const uint WaitObject0 = 0x00000000;
    internal const uint WaitAbandoned0 = 0x00000080;
    internal const uint WaitTimeout = 0x00000102;
    internal const uint WaitFailed = 0xFFFFFFFF;

    internal const int ErrorAccessDenied = 5;
    internal const int ErrorInvalidHandle = 6;
    internal const int ErrorAlreadyExists = 183;
    internal const int ErrorPrivilegeNotHeld = 1314;

    internal const uint SeKernelObject = 6;
    internal const uint OwnerSecurityInformation = 0x00000001;
    internal const uint DaclSecurityInformation = 0x00000004;

    internal const byte AccessAllowedAceType = 0;

    [StructLayout(LayoutKind.Sequential)]
    internal struct SecurityAttributes
    {
        public uint Length;
        public nint SecurityDescriptor;
        public int InheritHandle;
    }

    [LibraryImport("kernel32.dll", SetLastError = true, StringMarshalling = StringMarshalling.Utf16)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    internal static partial nint CreateMutexExW(
        in SecurityAttributes lpMutexAttributes,
        string lpName,
        uint dwFlags,
        uint dwDesiredAccess);

    [LibraryImport("kernel32.dll", SetLastError = true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool ReleaseMutex(nint hMutex);

    [LibraryImport("kernel32.dll", SetLastError = true, StringMarshalling = StringMarshalling.Utf16)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    internal static partial nint CreateEventW(
        nint lpEventAttributes,
        [MarshalAs(UnmanagedType.Bool)] bool bManualReset,
        [MarshalAs(UnmanagedType.Bool)] bool bInitialState,
        string? lpName);

    [LibraryImport("kernel32.dll", SetLastError = true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool SetEvent(nint hEvent);

    [LibraryImport("kernel32.dll", SetLastError = true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    internal static partial uint WaitForMultipleObjects(
        uint nCount,
        [In] nint[] lpHandles,
        [MarshalAs(UnmanagedType.Bool)] bool bWaitAll,
        uint dwMilliseconds);

    [LibraryImport("kernel32.dll", SetLastError = true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool CloseHandle(nint hObject);

    [LibraryImport("advapi32.dll", SetLastError = true, StringMarshalling = StringMarshalling.Utf16)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool ConvertStringSecurityDescriptorToSecurityDescriptorW(
        string stringSecurityDescriptor,
        uint stringSdRevision,
        out nint securityDescriptor,
        out uint securityDescriptorSize);

    [LibraryImport("advapi32.dll")]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    internal static partial uint GetSecurityInfo(
        nint handle,
        uint objectType,
        uint securityInfo,
        out nint ppsidOwner,
        out nint ppsidGroup,
        out nint ppDacl,
        out nint ppSacl,
        out nint ppSecurityDescriptor);
}
