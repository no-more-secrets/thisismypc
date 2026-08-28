using System.Runtime.InteropServices;

namespace ThisIsMyPC.Interop.Win32.Services;

internal static partial class NativeServiceControl
{
    // Access rights — minimal masks per operation. Protected services (e.g. WinDefend)
    // allow query but deny change; a maximal mask would make even Query fail on them.
    internal const uint SC_MANAGER_CONNECT = 0x0001;
    internal const uint SC_MANAGER_ENUMERATE_SERVICE = 0x0004;
    internal const uint SERVICE_QUERY_CONFIG = 0x0001;
    internal const uint SERVICE_CHANGE_CONFIG = 0x0002;
    internal const uint SERVICE_QUERY_STATUS = 0x0004;
    internal const uint SERVICE_START = 0x0010;
    internal const uint SERVICE_STOP = 0x0020;

    internal const uint SERVICE_NO_CHANGE = 0xFFFFFFFF;

    // dwStartType values
    internal const uint SERVICE_AUTO_START = 0x0002;
    internal const uint SERVICE_DEMAND_START = 0x0003;
    internal const uint SERVICE_DISABLED = 0x0004;

    // dwCurrentState values
    internal const uint SERVICE_STOPPED = 0x0001;
    internal const uint SERVICE_START_PENDING = 0x0002;
    internal const uint SERVICE_STOP_PENDING = 0x0003;
    internal const uint SERVICE_RUNNING = 0x0004;
    internal const uint SERVICE_CONTINUE_PENDING = 0x0005;
    internal const uint SERVICE_PAUSE_PENDING = 0x0006;
    internal const uint SERVICE_PAUSED = 0x0007;

    internal const uint SERVICE_CONTROL_STOP = 0x0001;
    internal const uint SC_STATUS_PROCESS_INFO = 0;
    internal const uint SERVICE_CONFIG_DESCRIPTION = 1;
    internal const uint SERVICE_CONFIG_DELAYED_AUTO_START_INFO = 3;

    // EnumServicesStatusExW parameters
    internal const int SC_ENUM_PROCESS_INFO = 0;
    internal const uint SERVICE_WIN32 = 0x00000030; // own-process | share-process (user services included)
    internal const uint SERVICE_STATE_ALL = 0x00000003;

    // Win32 errors
    internal const int ERROR_ACCESS_DENIED = 5;
    internal const int ERROR_INSUFFICIENT_BUFFER = 122;
    internal const int ERROR_MORE_DATA = 234;
    internal const int ERROR_SERVICE_ALREADY_RUNNING = 1056;
    internal const int ERROR_SERVICE_DOES_NOT_EXIST = 1060;
    internal const int ERROR_SERVICE_CANNOT_ACCEPT_CTRL = 1061;
    internal const int ERROR_SERVICE_NOT_ACTIVE = 1062;

    [StructLayout(LayoutKind.Sequential)]
    internal struct ServiceStatusProcess
    {
        public uint dwServiceType;
        public uint dwCurrentState;
        public uint dwControlsAccepted;
        public uint dwWin32ExitCode;
        public uint dwServiceSpecificExitCode;
        public uint dwCheckPoint;
        public uint dwWaitHint;
        public uint dwProcessId;
        public uint dwServiceFlags;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct ServiceStatus
    {
        public uint dwServiceType;
        public uint dwCurrentState;
        public uint dwControlsAccepted;
        public uint dwWin32ExitCode;
        public uint dwServiceSpecificExitCode;
        public uint dwCheckPoint;
        public uint dwWaitHint;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct EnumServiceStatusProcess
    {
        public nint lpServiceName;
        public nint lpDisplayName;
        public ServiceStatusProcess ServiceStatusProcess;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct QueryServiceConfig
    {
        public uint dwServiceType;
        public uint dwStartType;
        public uint dwErrorControl;
        public nint lpBinaryPathName;
        public nint lpLoadOrderGroup;
        public uint dwTagId;
        public nint lpDependencies;
        public nint lpServiceStartName;
        public nint lpDisplayName;
    }

    [LibraryImport("advapi32.dll", StringMarshalling = StringMarshalling.Utf16, SetLastError = true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    internal static partial nint OpenSCManagerW(string? lpMachineName, string? lpDatabaseName, uint dwDesiredAccess);

    [LibraryImport("advapi32.dll", StringMarshalling = StringMarshalling.Utf16, SetLastError = true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    internal static partial nint OpenServiceW(nint hSCManager, string lpServiceName, uint dwDesiredAccess);

    [LibraryImport("advapi32.dll", SetLastError = true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool CloseServiceHandle(nint hSCObject);

    [LibraryImport("advapi32.dll", SetLastError = true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool QueryServiceStatusEx(
        nint hService,
        uint infoLevel,
        out ServiceStatusProcess lpBuffer,
        uint cbBufSize,
        out uint pcbBytesNeeded);

    [LibraryImport("advapi32.dll", SetLastError = true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool QueryServiceConfigW(
        nint hService,
        nint lpServiceConfig,
        uint cbBufSize,
        out uint pcbBytesNeeded);

    [LibraryImport("advapi32.dll", SetLastError = true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool QueryServiceConfig2W(
        nint hService,
        uint dwInfoLevel,
        out int lpBuffer,
        uint cbBufSize,
        out uint pcbBytesNeeded);

    [LibraryImport("advapi32.dll", StringMarshalling = StringMarshalling.Utf16, SetLastError = true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool ChangeServiceConfigW(
        nint hService,
        uint dwServiceType,
        uint dwStartType,
        uint dwErrorControl,
        string? lpBinaryPathName,
        string? lpLoadOrderGroup,
        nint lpdwTagId,
        string? lpDependencies,
        string? lpServiceStartName,
        string? lpPassword,
        string? lpDisplayName);

    [LibraryImport("advapi32.dll", SetLastError = true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool ChangeServiceConfig2W(
        nint hService,
        uint dwInfoLevel,
        ref int lpInfo);

    [LibraryImport("advapi32.dll", SetLastError = true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool ControlService(
        nint hService,
        uint dwControl,
        out ServiceStatus lpServiceStatus);

    [LibraryImport("advapi32.dll", StringMarshalling = StringMarshalling.Utf16, SetLastError = true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool StartServiceW(
        nint hService,
        uint dwNumServiceArgs,
        nint lpServiceArgVectors);

    // Buffer-based overload for variable-size info levels (e.g. SERVICE_CONFIG_DESCRIPTION)
    [LibraryImport("advapi32.dll", SetLastError = true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool QueryServiceConfig2W(
        nint hService,
        uint dwInfoLevel,
        nint lpBuffer,
        uint cbBufSize,
        out uint pcbBytesNeeded);

    [LibraryImport("advapi32.dll", StringMarshalling = StringMarshalling.Utf16, SetLastError = true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool EnumServicesStatusExW(
        nint hSCManager,
        int infoLevel,
        uint dwServiceType,
        uint dwServiceState,
        nint lpServices,
        uint cbBufSize,
        out uint pcbBytesNeeded,
        out uint lpServicesReturned,
        ref uint lpResumeHandle,
        string? pszGroupName);
}
