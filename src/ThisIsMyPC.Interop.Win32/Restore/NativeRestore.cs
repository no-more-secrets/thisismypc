using System.Runtime.InteropServices;

namespace ThisIsMyPC.Interop.Win32.Restore;

internal static partial class NativeRestore
{
    // RESTOREPOINTINFO.dwEventType
    internal const int BEGIN_SYSTEM_CHANGE = 100;
    internal const int END_SYSTEM_CHANGE = 101;

    // RESTOREPOINTINFO.dwRestorePtType
    internal const int MODIFY_SETTINGS = 12;

    internal const uint ERROR_SUCCESS = 0;
    internal const uint ERROR_SERVICE_DISABLED = 1058;

    // MAX_DESC_W: 256 wide chars including the null terminator
    internal const int MaxDescriptionChars = 256;

    [StructLayout(LayoutKind.Sequential)]
    internal unsafe struct RESTOREPOINTINFOW
    {
        internal int dwEventType;
        internal int dwRestorePtType;
        internal long llSequenceNumber;
        internal fixed char szDescription[MaxDescriptionChars];
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct STATEMGRSTATUS
    {
        internal uint nStatus;
        internal long llSequenceNumber;
    }

    // Blittable structs passed by pointer; no marshalling generated, NativeAOT-safe.
    [LibraryImport("srclient.dll")]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static unsafe partial bool SRSetRestorePointW(
        RESTOREPOINTINFOW* restorePointSpec,
        STATEMGRSTATUS* status);
}
