using System.Runtime.InteropServices;

namespace ThisIsMyPC.Interop.Win32;

/// <summary>
/// advapi32 token calls behind <see cref="ShellLauncher"/>: read the current
/// process's elevation, fetch the linked (unelevated) token an elevated
/// admin token carries, and start a process with it.
/// </summary>
internal static unsafe partial class NativeProcessToken
{
    internal const uint TOKEN_QUERY = 0x0008;
    internal const uint TOKEN_DUPLICATE = 0x0002;
    internal const uint TOKEN_ASSIGN_PRIMARY = 0x0001;
    internal const uint TOKEN_IMPERSONATE = 0x0004;
    internal const uint MAXIMUM_ALLOWED = 0x02000000;

    // TOKEN_INFORMATION_CLASS
    internal const int TokenUser = 1;
    internal const int TokenSessionId = 12;
    internal const int TokenElevationType = 18;
    internal const int TokenLinkedToken = 19;

    // TOKEN_ELEVATION_TYPE
    internal const int TokenElevationTypeDefault = 1;
    internal const int TokenElevationTypeFull = 2;
    internal const int TokenElevationTypeLimited = 3;

    // SECURITY_IMPERSONATION_LEVEL / TOKEN_TYPE
    internal const int SecurityImpersonation = 2;
    internal const int TokenPrimary = 1;

    internal const uint LOGON_WITH_PROFILE = 0x00000001;

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    internal struct STARTUPINFOW
    {
        public uint cb;
        public nint lpReserved;
        public nint lpDesktop;
        public nint lpTitle;
        public uint dwX;
        public uint dwY;
        public uint dwXSize;
        public uint dwYSize;
        public uint dwXCountChars;
        public uint dwYCountChars;
        public uint dwFillAttribute;
        public uint dwFlags;
        public ushort wShowWindow;
        public ushort cbReserved2;
        public nint lpReserved2;
        public nint hStdInput;
        public nint hStdOutput;
        public nint hStdError;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct PROCESS_INFORMATION
    {
        public nint hProcess;
        public nint hThread;
        public uint dwProcessId;
        public uint dwThreadId;
    }

    [LibraryImport("kernel32.dll")]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    internal static partial nint GetCurrentProcess();

    [LibraryImport("kernel32.dll", SetLastError = true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool CloseHandle(nint hObject);

    [LibraryImport("advapi32.dll", SetLastError = true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool OpenProcessToken(nint processHandle, uint desiredAccess, out nint tokenHandle);

    [LibraryImport("advapi32.dll", SetLastError = true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool GetTokenInformation(
        nint tokenHandle, int tokenInformationClass, void* tokenInformation, uint tokenInformationLength, out uint returnLength);

    [LibraryImport("advapi32.dll", SetLastError = true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool DuplicateTokenEx(
        nint existingToken, uint desiredAccess, nint tokenAttributes, int impersonationLevel, int tokenType, out nint newToken);

    [LibraryImport("advapi32.dll", SetLastError = true, StringMarshalling = StringMarshalling.Utf16)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool CreateProcessWithTokenW(
        nint token, uint logonFlags, string? applicationName, char* commandLine, uint creationFlags,
        nint environment, string? currentDirectory, STARTUPINFOW* startupInfo, PROCESS_INFORMATION* processInformation);

    [LibraryImport("advapi32.dll", SetLastError = true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool ImpersonateLoggedOnUser(nint token);

    [LibraryImport("advapi32.dll", SetLastError = true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool RevertToSelf();

    // TOKEN_USER is a SID_AND_ATTRIBUTES: a pointer to the SID, then its attributes.
    [StructLayout(LayoutKind.Sequential)]
    internal struct TOKEN_USER
    {
        public nint Sid;
        public uint Attributes;
    }

    [LibraryImport("advapi32.dll", SetLastError = true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool ConvertSidToStringSidW(nint sid, out nint stringSid);

    [LibraryImport("advapi32.dll", SetLastError = true, StringMarshalling = StringMarshalling.Utf16)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool LookupAccountSidW(
        string? systemName, nint sid, char* name, ref uint cchName, char* referencedDomain, ref uint cchReferencedDomain, out int use);

    [LibraryImport("kernel32.dll", SetLastError = true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    internal static partial nint LocalFree(nint mem);
}
