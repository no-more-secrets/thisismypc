using System.Runtime.InteropServices;

namespace ThisIsMyPC.Interop.Win32.Power;

internal static partial class NativePower
{
    // POWER_DATA_ACCESSOR — enumerate power scheme GUIDs
    internal const uint ACCESS_SCHEME = 16;

    // powrprof APIs return the Win32 error code directly (not via GetLastError)
    internal const uint ERROR_SUCCESS = 0;
    internal const uint ERROR_FILE_NOT_FOUND = 2;
    internal const uint ERROR_ACCESS_DENIED = 5;
    internal const uint ERROR_NO_MORE_ITEMS = 259;

    [LibraryImport("powrprof.dll")]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    internal static partial uint PowerEnumerate(
        nint rootPowerKey,
        nint schemeGuid,
        nint subGroupOfPowerSettingsGuid,
        uint accessFlags,
        uint index,
        byte[]? buffer,
        ref uint bufferSize);

    [LibraryImport("powrprof.dll")]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    internal static partial uint PowerGetActiveScheme(
        nint userRootPowerKey,
        out nint activePolicyGuid);

    [LibraryImport("powrprof.dll")]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    internal static partial uint PowerSetActiveScheme(
        nint userRootPowerKey,
        in Guid schemeGuid);

    [LibraryImport("powrprof.dll")]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    internal static partial uint PowerReadFriendlyName(
        nint rootPowerKey,
        in Guid schemeGuid,
        nint subGroupOfPowerSettingsGuid,
        nint powerSettingGuid,
        byte[]? buffer,
        ref uint bufferSize);

    [LibraryImport("powrprof.dll")]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    internal static partial uint PowerReadDescription(
        nint rootPowerKey,
        in Guid schemeGuid,
        nint subGroupOfPowerSettingsGuid,
        nint powerSettingGuid,
        byte[]? buffer,
        ref uint bufferSize);

    [LibraryImport("kernel32.dll")]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    internal static partial nint LocalFree(nint hMem);
}
