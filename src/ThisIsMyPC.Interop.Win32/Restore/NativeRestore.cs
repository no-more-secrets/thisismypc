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
    internal const uint ERROR_ACCESS_DISABLED_BY_POLICY = 1260;
    internal const uint ERROR_INVALID_DRIVE = 15;

    // MAX_DESC_W: 256 wide chars including the null terminator
    internal const int MaxDescriptionChars = 256;

    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    internal unsafe struct RESTOREPOINTINFOW
    {
        internal int dwEventType;
        internal int dwRestorePtType;
        internal long llSequenceNumber;
        internal fixed char szDescription[MaxDescriptionChars];
    }

    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    internal struct STATEMGRSTATUS
    {
        internal uint nStatus;
        internal long llSequenceNumber;
    }

    // Native export verified from srclient.dll. Returns a Win32 status, zero on success.
    [LibraryImport("srclient.dll", StringMarshalling = StringMarshalling.Utf16)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    internal static partial uint EnableSR(string drive);

    // Blittable structs passed by pointer; no marshalling generated, NativeAOT-safe.
    [LibraryImport("srclient.dll")]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static unsafe partial bool SRSetRestorePointW(
        RESTOREPOINTINFOW* restorePointSpec,
        STATEMGRSTATUS* status);
}
