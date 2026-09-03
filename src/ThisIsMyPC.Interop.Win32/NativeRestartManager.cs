using System.Runtime.InteropServices;

namespace ThisIsMyPC.Interop.Win32;

/// <summary>
/// rstrtmgr.dll: the Restart Manager shuts an application down and starts it
/// again as the same user, and it knows the shell. ExplorerPatcher restarts
/// Explorer this way (BeginExplorerRestart / FinishExplorerRestart in its
/// utility.h), and it is far quicker than asking the tray to quit and
/// waiting for it.
/// </summary>
internal static unsafe partial class NativeRestartManager
{
    internal const int CCH_RM_SESSION_KEY = 32;
    internal const uint RmForceShutdown = 0x1;
    internal const uint RmRebootReasonNone = 0;
    internal const uint ERROR_SUCCESS = 0;
    internal const uint ERROR_MORE_DATA = 234;
    internal const uint RM_INVALID_SESSION = 0xFFFFFFFF;

    [StructLayout(LayoutKind.Sequential)]
    internal struct RM_UNIQUE_PROCESS
    {
        public uint dwProcessId;
        public uint ProcessStartTimeLow;
        public uint ProcessStartTimeHigh;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    internal struct RM_PROCESS_INFO
    {
        public RM_UNIQUE_PROCESS Process;
        public fixed char strAppName[256];
        public fixed char strServiceShortName[64];
        public uint ApplicationType;
        public uint AppStatus;
        public uint TSSessionId;
        public int bRestartable;
    }

    [LibraryImport("rstrtmgr.dll")]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    internal static partial uint RmStartSession(out uint pSessionHandle, uint dwSessionFlags, char* strSessionKey);

    [LibraryImport("rstrtmgr.dll")]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    internal static partial uint RmRegisterResources(
        uint dwSessionHandle, uint nFiles, nint rgsFileNames, uint nApplications, RM_UNIQUE_PROCESS* rgApplications,
        uint nServices, nint rgsServiceNames);

    [LibraryImport("rstrtmgr.dll")]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    internal static partial uint RmGetList(
        uint dwSessionHandle, out uint pnProcInfoNeeded, ref uint pnProcInfo, RM_PROCESS_INFO* rgAffectedApps, out uint lpdwRebootReasons);

    [LibraryImport("rstrtmgr.dll")]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    internal static partial uint RmShutdown(uint dwSessionHandle, uint lActionFlags, nint fnStatus);

    [LibraryImport("rstrtmgr.dll")]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    internal static partial uint RmRestart(uint dwSessionHandle, uint dwRestartFlags, nint fnStatus);

    [LibraryImport("rstrtmgr.dll")]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    internal static partial uint RmEndSession(uint dwSessionHandle);
}
