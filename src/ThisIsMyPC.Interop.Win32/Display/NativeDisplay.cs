using System.Runtime.InteropServices;

namespace ThisIsMyPC.Interop.Win32.Display;

internal static partial class NativeDisplay
{
    internal const int PHYSICAL_MONITOR_DESCRIPTION_SIZE = 128;

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    internal struct PHYSICAL_MONITOR
    {
        public nint hPhysicalMonitor;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = PHYSICAL_MONITOR_DESCRIPTION_SIZE)]
        public string szPhysicalMonitorDescription;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    internal struct MONITORINFOEXW
    {
        public uint cbSize;
        public RECT rcMonitor;
        public RECT rcWork;
        public uint dwFlags;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)]
        public string szDevice;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct RECT
    {
        public int Left, Top, Right, Bottom;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct SYSTEM_POWER_STATUS
    {
        public byte ACLineStatus;
        public byte BatteryFlag;
        public byte BatteryLifePercent;
        public byte SystemStatusFlag;
        public uint BatteryLifeTime;
        public uint BatteryFullLifeTime;
    }

    // EnumDisplayMonitors uses a native callback; DllImport with a delegate is
    // the simplest marshaling that stays NativeAOT-safe via [UnmanagedCallersOnly]
    // alternatives being overkill here (the delegate is kept alive for the call).
    internal delegate int MonitorEnumProc(nint hMonitor, nint hdc, nint lprcMonitor, nint dwData);

    [DllImport("user32.dll")]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    internal static extern int EnumDisplayMonitors(nint hdc, nint lprcClip, MonitorEnumProc lpfnEnum, nint dwData);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    internal static extern int GetMonitorInfoW(nint hMonitor, ref MONITORINFOEXW lpmi);

    [DllImport("dxva2.dll", SetLastError = true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    internal static extern int GetNumberOfPhysicalMonitorsFromHMONITOR(nint hMonitor, out uint count);

    [DllImport("dxva2.dll", SetLastError = true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    internal static extern int GetPhysicalMonitorsFromHMONITOR(
        nint hMonitor, uint count, [Out] PHYSICAL_MONITOR[] monitors);

    [DllImport("dxva2.dll", SetLastError = true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    internal static extern int DestroyPhysicalMonitors(uint count, PHYSICAL_MONITOR[] monitors);

    [DllImport("dxva2.dll", SetLastError = true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    internal static extern int GetVCPFeatureAndVCPFeatureReply(
        nint hMonitor, byte vcpCode, out uint type, out uint currentValue, out uint maximumValue);

    [DllImport("dxva2.dll", SetLastError = true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    internal static extern int SetVCPFeature(nint hMonitor, byte vcpCode, uint newValue);

    [DllImport("dxva2.dll", SetLastError = true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    internal static extern int GetCapabilitiesStringLength(nint hMonitor, out uint length);

    [DllImport("dxva2.dll", SetLastError = true, CharSet = CharSet.Ansi, BestFitMapping = false)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    internal static extern int CapabilitiesRequestAndCapabilitiesReply(
        nint hMonitor, [Out] byte[] capabilities, uint length);

    [DllImport("kernel32.dll")]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    internal static extern int GetSystemPowerStatus(out SYSTEM_POWER_STATUS status);

    internal const uint EDD_GET_DEVICE_INTERFACE_NAME = 0x1;

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    internal struct DISPLAY_DEVICEW
    {
        public uint cb;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)]
        public string DeviceName;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)]
        public string DeviceString;
        public uint StateFlags;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)]
        public string DeviceID;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)]
        public string DeviceKey;
    }

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    internal static extern int EnumDisplayDevicesW(
        string? lpDevice, uint iDevNum, ref DISPLAY_DEVICEW lpDisplayDevice, uint dwFlags);
}
