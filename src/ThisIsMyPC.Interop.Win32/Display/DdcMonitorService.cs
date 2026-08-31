using System.Text;
using ThisIsMyPC.Core.Display;
using ThisIsMyPC.Core.Results;
using ThisIsMyPC.Core.Services;

namespace ThisIsMyPC.Interop.Win32.Display;

/// <summary>
/// DDC/CI monitor access via dxva2.dll. Physical monitor handles are resolved
/// fresh for every operation and destroyed immediately: handles go stale across
/// display changes, sleep, and driver resets, and re-enumeration costs
/// milliseconds while a stale-handle failure costs a confused user.
/// </summary>
public sealed class DdcMonitorService : IMonitorService
{
    private const byte VcpBrightness = 0x10;
    private const byte VcpContrast = 0x12;
    private const byte VcpInputSource = 0x60;

    /// <summary>
    /// Names for vendor VCP codes (0xE0-0xFF are manufacturer-specific per
    /// MCCS). Verified on ASUS monitors; unknown codes render as "Feature 0xNN"
    /// so a wrong guess never mislabels a control.
    /// </summary>
    private static readonly Dictionary<int, string> VendorFeatureNames = new()
    {
        [0xE6] = "Blue light filter",
    };

    /// <summary>MCCS input source names; unknown values render as "Input 0xNN".</summary>
    private static readonly Dictionary<int, string> InputNames = new()
    {
        [0x01] = "VGA 1",
        [0x02] = "VGA 2",
        [0x03] = "DVI 1",
        [0x04] = "DVI 2",
        [0x0F] = "DisplayPort 1",
        [0x10] = "DisplayPort 2",
        [0x11] = "HDMI 1",
        [0x12] = "HDMI 2",
        [0x1B] = "USB-C",
    };

    public OperationResult<IReadOnlyList<MonitorDevice>> EnumerateMonitors()
    {
        try
        {
            var devices = new List<MonitorDevice>();

            foreach (var (physical, deviceName, index) in EnumeratePhysical())
            {
                try
                {
                    devices.Add(ReadDevice(physical, deviceName, index));
                }
                finally
                {
                    DestroyOne(physical);
                }
            }

            return OperationResult<IReadOnlyList<MonitorDevice>>.Success(devices);
        }
        catch (Exception ex) when (ex is DllNotFoundException or EntryPointNotFoundException)
        {
            return OperationResult<IReadOnlyList<MonitorDevice>>.Failure(
                $"DDC/CI is unavailable on this system: {ex.Message}", ErrorCategory.ServiceUnavailable, ex);
        }
    }

    public OperationResult<bool> SetBrightness(string monitorId, int value) =>
        SetVcp(monitorId, VcpBrightness, value);

    public OperationResult<bool> SetContrast(string monitorId, int value) =>
        SetVcp(monitorId, VcpContrast, value);

    public OperationResult<bool> SetInputSource(string monitorId, int value) =>
        SetVcp(monitorId, VcpInputSource, value);

    public OperationResult<bool> SetVcpValue(string monitorId, int vcpCode, int value) =>
        SetVcp(monitorId, (byte)vcpCode, value);

    public bool HasSystemBattery()
    {
        if (NativeDisplay.GetSystemPowerStatus(out var status) == 0)
            return false;
        // BatteryFlag 128 = no system battery, 255 = unknown.
        return status.BatteryFlag is not (128 or 255);
    }

    private static MonitorDevice ReadDevice(
        NativeDisplay.PHYSICAL_MONITOR physical, string deviceName, int index)
    {
        var id = $"{deviceName}|{index}";
        var name = physical.szPhysicalMonitorDescription is { Length: > 0 } d
            ? d : deviceName.TrimStart('\\', '.');

        // Brightness answers on effectively every DDC-capable monitor; treat a
        // refusal here as "no DDC" rather than probing the whole VCP table.
        if (NativeDisplay.GetVCPFeatureAndVCPFeatureReply(
                physical.hPhysicalMonitor, VcpBrightness, out _, out var bright, out var brightMax) == 0)
        {
            return new MonitorDevice
            {
                Id = id,
                Name = name,
                SupportsDdc = false,
                DdcError = "This monitor did not answer DDC/CI. Some monitors need it enabled in their on-screen menu.",
            };
        }

        int? contrast = null;
        var contrastMax = 100;
        if (NativeDisplay.GetVCPFeatureAndVCPFeatureReply(
                physical.hPhysicalMonitor, VcpContrast, out _, out var c, out var cMax) != 0)
        {
            contrast = (int)c;
            contrastMax = (int)Math.Max(1, cMax);
        }

        int? currentInput = null;
        if (NativeDisplay.GetVCPFeatureAndVCPFeatureReply(
                physical.hPhysicalMonitor, VcpInputSource, out _, out var input, out _) != 0)
        {
            // High bytes carry model-specific flags on some monitors; MCCS
            // input values live in the low byte.
            currentInput = (int)(input & 0xFF);
        }

        // One capabilities request (slow, scan-time only) feeds both the input
        // list and the vendor feature rows.
        var codeValues = ReadCodeValueGroups(physical.hPhysicalMonitor);

        return new MonitorDevice
        {
            Id = id,
            Name = name,
            SupportsDdc = true,
            Brightness = (int)bright,
            BrightnessMax = (int)Math.Max(1, brightMax),
            Contrast = contrast,
            ContrastMax = contrastMax,
            CurrentInput = currentInput,
            InputSources = BuildInputSources(codeValues, currentInput),
            VendorFeatures = BuildVendorFeatures(physical.hPhysicalMonitor, codeValues),
        };
    }

    private static IReadOnlyDictionary<int, IReadOnlyList<int>> ReadCodeValueGroups(nint handle)
    {
        if (NativeDisplay.GetCapabilitiesStringLength(handle, out var length) == 0 || length <= 1)
            return new Dictionary<int, IReadOnlyList<int>>();

        var buffer = new byte[length];
        if (NativeDisplay.CapabilitiesRequestAndCapabilitiesReply(handle, buffer, length) == 0)
            return new Dictionary<int, IReadOnlyList<int>>();

        var capabilities = Encoding.ASCII.GetString(buffer).TrimEnd('\0');
        return VcpCapabilities.ParseCodeValueGroups(capabilities);
    }

    /// <summary>A monitor with a current input but no capabilities still gets that one entry.</summary>
    private static IReadOnlyList<MonitorInputSource> BuildInputSources(
        IReadOnlyDictionary<int, IReadOnlyList<int>> codeValues, int? currentInput)
    {
        var values = new List<int>();
        if (codeValues.TryGetValue(VcpInputSource, out var declared))
            values.AddRange(declared);
        if (values.Count == 0 && currentInput is { } current)
            values.Add(current);

        return values
            .Distinct()
            .Order()
            .Select(v => new MonitorInputSource(
                v, InputNames.TryGetValue(v, out var n) ? n : $"Input 0x{v:X2}"))
            .ToList();
    }

    /// <summary>
    /// Vendor codes (0xE0-0xFF) with a declared value list become controls.
    /// The current value read costs one GetVCP per feature; a refusal drops
    /// the feature rather than showing a control that cannot work.
    /// </summary>
    private static IReadOnlyList<VendorVcpFeature> BuildVendorFeatures(
        nint handle, IReadOnlyDictionary<int, IReadOnlyList<int>> codeValues)
    {
        var features = new List<VendorVcpFeature>();
        foreach (var (code, values) in codeValues.OrderBy(p => p.Key))
        {
            if (code is < 0xE0 or > 0xFF || values.Count < 2)
                continue;

            if (NativeDisplay.GetVCPFeatureAndVCPFeatureReply(handle, (byte)code, out _, out var current, out _) == 0)
                continue;

            var isNamed = VendorFeatureNames.TryGetValue(code, out var name);
            features.Add(new VendorVcpFeature(
                code,
                isNamed ? name! : $"Feature 0x{code:X2}",
                values.Distinct().Order().ToList(),
                (int)(current & 0xFF),
                isNamed));
        }

        return features;
    }

    private OperationResult<bool> SetVcp(string monitorId, byte code, int value)
    {
        try
        {
            foreach (var (physical, deviceName, index) in EnumeratePhysical())
            {
                try
                {
                    if ($"{deviceName}|{index}" != monitorId)
                        continue;

                    if (NativeDisplay.SetVCPFeature(physical.hPhysicalMonitor, code, (uint)value) == 0)
                    {
                        return OperationResult<bool>.Failure(
                            "The monitor rejected the DDC/CI write. Rescan and try again.",
                            ErrorCategory.ServiceUnavailable);
                    }

                    return OperationResult<bool>.Success(true);
                }
                finally
                {
                    DestroyOne(physical);
                }
            }

            return OperationResult<bool>.Failure(
                "Monitor not found. It may have been unplugged; rescan the module.",
                ErrorCategory.NotFound);
        }
        catch (Exception ex) when (ex is DllNotFoundException or EntryPointNotFoundException)
        {
            return OperationResult<bool>.Failure(
                $"DDC/CI is unavailable on this system: {ex.Message}", ErrorCategory.ServiceUnavailable, ex);
        }
    }

    /// <summary>Yields every physical monitor with its adapter device name and per-adapter index.</summary>
    private static IEnumerable<(NativeDisplay.PHYSICAL_MONITOR Physical, string DeviceName, int Index)> EnumeratePhysical()
    {
        var hmonitors = new List<nint>();
        NativeDisplay.MonitorEnumProc proc = (hMonitor, _, _, _) =>
        {
            hmonitors.Add(hMonitor);
            return 1;
        };
        _ = NativeDisplay.EnumDisplayMonitors(0, 0, proc, 0);
        GC.KeepAlive(proc);

        foreach (var hMonitor in hmonitors)
        {
            var info = new NativeDisplay.MONITORINFOEXW();
            info.cbSize = (uint)System.Runtime.InteropServices.Marshal.SizeOf<NativeDisplay.MONITORINFOEXW>();
            var deviceName = NativeDisplay.GetMonitorInfoW(hMonitor, ref info) != 0
                ? info.szDevice : $"HMON:{hMonitor}";

            if (NativeDisplay.GetNumberOfPhysicalMonitorsFromHMONITOR(hMonitor, out var count) == 0 || count == 0)
                continue;

            var physicals = new NativeDisplay.PHYSICAL_MONITOR[count];
            if (NativeDisplay.GetPhysicalMonitorsFromHMONITOR(hMonitor, count, physicals) == 0)
                continue;

            for (var i = 0; i < physicals.Length; i++)
                yield return (physicals[i], deviceName, i);
        }
    }

    private static void DestroyOne(NativeDisplay.PHYSICAL_MONITOR physical) =>
        _ = NativeDisplay.DestroyPhysicalMonitors(1, [physical]);
}
