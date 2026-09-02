using System.Runtime.InteropServices;

namespace ThisIsMyPC.Interop.Win32.Power;

internal static partial class NativePower
{
    // POWER_DATA_ACCESSOR values
    internal const uint ACCESS_SCHEME = 16;
    internal const uint ACCESS_SUBGROUP = 17;
    internal const uint ACCESS_INDIVIDUAL_SETTING = 18;

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

    /// <summary>Duplicates a scheme; *destinationSchemeGuid is LocalAlloc'd; free with LocalFree.</summary>
    [LibraryImport("powrprof.dll")]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    internal static partial uint PowerDuplicateScheme(nint rootPowerKey, in Guid sourceSchemeGuid, out nint destinationSchemeGuid);

    /// <summary>Same entry point with a caller-supplied destination: *destinationSchemeGuid points at the GUID to create.</summary>
    [LibraryImport("powrprof.dll", EntryPoint = "PowerDuplicateScheme")]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    internal static partial uint PowerDuplicateSchemeTo(nint rootPowerKey, in Guid sourceSchemeGuid, ref nint destinationSchemeGuid);

    [LibraryImport("powrprof.dll")]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    internal static partial uint PowerDeleteScheme(nint rootPowerKey, in Guid schemeGuid);

    /// <summary>Recreates a stock scheme from Windows' default store, or resets it to defaults when present.</summary>
    [LibraryImport("powrprof.dll")]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    internal static partial uint PowerRestoreIndividualDefaultPowerScheme(in Guid schemeGuid);

    /// <summary>Buffer is UTF-16 including the terminating null.</summary>
    [LibraryImport("powrprof.dll")]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    internal static partial uint PowerWriteFriendlyName(
        nint rootPowerKey, in Guid schemeGuid, nint subGroupGuid, nint settingGuid,
        [In] byte[] buffer, uint bufferSize);

    /// <summary>Buffer is UTF-16 including the terminating null.</summary>
    [LibraryImport("powrprof.dll")]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    internal static partial uint PowerWriteDescription(
        nint rootPowerKey, in Guid schemeGuid, nint subGroupGuid, nint settingGuid,
        [In] byte[] buffer, uint bufferSize);

    // POWER_INFORMATION_LEVEL: SystemReserveHiberFile toggles hibernation like
    // powercfg /hibernate on|off. Returns NTSTATUS (0 = success), not Win32.
    internal const int SystemReserveHiberFile = 10;

    [LibraryImport("powrprof.dll")]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    internal static partial uint CallNtPowerInformation(
        int informationLevel, ref byte inputBuffer, uint inputBufferLength,
        nint outputBuffer, uint outputBufferLength);

    // ---- Per-plan setting enumeration (Story 4.2) ----

    [LibraryImport("powrprof.dll")]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    internal static partial uint PowerEnumerate(
        nint rootPowerKey,
        in Guid schemeGuid,
        nint subGroupOfPowerSettingsGuid,
        uint accessFlags,
        uint index,
        byte[]? buffer,
        ref uint bufferSize);

    [LibraryImport("powrprof.dll")]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    internal static partial uint PowerEnumerate(
        nint rootPowerKey,
        in Guid schemeGuid,
        in Guid subGroupOfPowerSettingsGuid,
        uint accessFlags,
        uint index,
        byte[]? buffer,
        ref uint bufferSize);

    [LibraryImport("powrprof.dll")]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    internal static partial uint PowerReadFriendlyName(
        nint rootPowerKey,
        in Guid schemeGuid,
        in Guid subGroupOfPowerSettingsGuid,
        nint powerSettingGuid,
        byte[]? buffer,
        ref uint bufferSize);

    [LibraryImport("powrprof.dll")]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    internal static partial uint PowerReadFriendlyName(
        nint rootPowerKey,
        in Guid schemeGuid,
        in Guid subGroupOfPowerSettingsGuid,
        in Guid powerSettingGuid,
        byte[]? buffer,
        ref uint bufferSize);

    [LibraryImport("powrprof.dll")]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    internal static partial uint PowerReadDescription(
        nint rootPowerKey,
        in Guid schemeGuid,
        in Guid subGroupOfPowerSettingsGuid,
        in Guid powerSettingGuid,
        byte[]? buffer,
        ref uint bufferSize);

    [LibraryImport("powrprof.dll")]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    internal static partial uint PowerReadACValueIndex(
        nint rootPowerKey,
        in Guid schemeGuid,
        in Guid subGroupOfPowerSettingsGuid,
        in Guid powerSettingGuid,
        out uint acValueIndex);

    [LibraryImport("powrprof.dll")]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    internal static partial uint PowerReadDCValueIndex(
        nint rootPowerKey,
        in Guid schemeGuid,
        in Guid subGroupOfPowerSettingsGuid,
        in Guid powerSettingGuid,
        out uint dcValueIndex);

    [LibraryImport("powrprof.dll")]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    internal static partial uint PowerWriteACValueIndex(
        nint rootPowerKey,
        in Guid schemeGuid,
        in Guid subGroupOfPowerSettingsGuid,
        in Guid powerSettingGuid,
        uint acValueIndex);

    [LibraryImport("powrprof.dll")]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    internal static partial uint PowerWriteDCValueIndex(
        nint rootPowerKey,
        in Guid schemeGuid,
        in Guid subGroupOfPowerSettingsGuid,
        in Guid powerSettingGuid,
        uint dcValueIndex);

    [LibraryImport("powrprof.dll")]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [return: MarshalAs(UnmanagedType.U1)]
    internal static partial bool PowerIsSettingRangeDefined(
        nint rootPowerKey,
        in Guid subGroupOfPowerSettingsGuid,
        in Guid powerSettingGuid);

    [LibraryImport("powrprof.dll")]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    internal static partial uint PowerReadValueMin(
        nint rootPowerKey,
        in Guid subGroupOfPowerSettingsGuid,
        in Guid powerSettingGuid,
        out uint valueMinimum);

    [LibraryImport("powrprof.dll")]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    internal static partial uint PowerReadValueMax(
        nint rootPowerKey,
        in Guid subGroupOfPowerSettingsGuid,
        in Guid powerSettingGuid,
        out uint valueMaximum);

    [LibraryImport("powrprof.dll")]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    internal static partial uint PowerReadValueIncrement(
        nint rootPowerKey,
        in Guid subGroupOfPowerSettingsGuid,
        in Guid powerSettingGuid,
        out uint valueIncrement);

    [LibraryImport("powrprof.dll")]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    internal static partial uint PowerReadValueUnitsSpecifier(
        nint rootPowerKey,
        in Guid subGroupOfPowerSettingsGuid,
        in Guid powerSettingGuid,
        byte[]? buffer,
        ref uint bufferSize);

    [LibraryImport("powrprof.dll")]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    internal static partial uint PowerReadPossibleFriendlyName(
        nint rootPowerKey,
        in Guid subGroupOfPowerSettingsGuid,
        in Guid powerSettingGuid,
        uint possibleSettingIndex,
        byte[]? buffer,
        ref uint bufferSize);

    [LibraryImport("powrprof.dll")]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [return: MarshalAs(UnmanagedType.U1)]
    internal static partial bool GetPwrCapabilities(out SystemPowerCapabilities capabilities);

    /// <summary>
    /// SYSTEM_POWER_CAPABILITIES (76 bytes). Only AoAc (Modern Standby support) is
    /// consumed, but the layout must be ABI-complete so the native write fits.
    /// </summary>
    [StructLayout(LayoutKind.Sequential)]
    internal struct SystemPowerCapabilities
    {
        public byte PowerButtonPresent;
        public byte SleepButtonPresent;
        public byte LidPresent;
        public byte SystemS1;
        public byte SystemS2;
        public byte SystemS3;
        public byte SystemS4;
        public byte SystemS5;
        public byte HiberFilePresent;
        public byte FullWake;
        public byte VideoDimPresent;
        public byte ApmPresent;
        public byte UpsPresent;
        public byte ThermalControl;
        public byte ProcessorThrottle;
        public byte ProcessorMinThrottle;
        public byte ProcessorMaxThrottle;
        public byte FastSystemS4;
        public byte Hiberboot;
        public byte WakeAlarmPresent;
        public byte AoAc;
        public byte DiskSpinDown;
        public byte HiberFileType;
        public byte AoAcConnectivitySupported;
        public byte Spare3_0;
        public byte Spare3_1;
        public byte Spare3_2;
        public byte Spare3_3;
        public byte Spare3_4;
        public byte Spare3_5;
        public byte SystemBatteriesPresent;
        public byte BatteriesAreShortTerm;
        public uint BatteryScale0Granularity;
        public uint BatteryScale0Capacity;
        public uint BatteryScale1Granularity;
        public uint BatteryScale1Capacity;
        public uint BatteryScale2Granularity;
        public uint BatteryScale2Capacity;
        public int AcOnLineWake;
        public int SoftLidWake;
        public int RtcWake;
        public int MinDeviceWakeState;
        public int DefaultLowLatencyWake;
    }
}
