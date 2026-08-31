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
    private const byte VcpPowerMode = 0xD6;

    // Session memory of successful writes, keyed monitor id + VCP code.
    // Monitors forget DDC state across sleep; ReapplyLastWrites pushes these
    // back after a resume or display change. Input source is deliberately
    // excluded: silently re-switching inputs after wake would fight the user.
    private readonly System.Collections.Concurrent.ConcurrentDictionary<(string Id, byte Code), int> _lastWrites = new();

    /// <summary>
    /// The capabilities request and the probe of every declared vendor code
    /// dominate scan time (a refusing code stalls for a DDC timeout each).
    /// Neither changes while the same monitor sits on the same port, so
    /// rescans reuse the first scan's answers; keyed by id + model name so
    /// a swapped monitor misses the cache.
    /// </summary>
    private sealed record CachedCapabilities(
        IReadOnlyDictionary<int, IReadOnlyList<int>> CodeValues,
        IReadOnlyList<int> AnsweringVendorCodes);

    private readonly System.Collections.Concurrent.ConcurrentDictionary<string, CachedCapabilities> _capabilitiesCache = new();

    /// <summary>
    /// Names for vendor VCP codes (0xE0-0xFF are manufacturer-specific per
    /// MCCS). Verified on ASUS monitors; unknown codes render as "Feature 0xNN"
    /// so a wrong guess never mislabels a control.
    /// </summary>
    private static readonly Dictionary<int, string> VendorFeatureNames = new()
    {
        [0xE6] = "Blue light filter",
    };

    /// <summary>
    /// Side effects worth warning about, shown as the row's tooltip.
    /// Diagnosed live on a PG27UCDM: at levels 3-4 the monitor accepts
    /// brightness writes, reports success, and ignores them.
    /// </summary>
    private static readonly Dictionary<int, string> VendorFeatureHints = new()
    {
        [0xE6] = "Levels 3 and 4 lock brightness on many ASUS monitors; the brightness slider stops working until this is lowered.",
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

    public OperationResult<bool> ReapplyLastWrites()
    {
        var failures = 0;
        foreach (var ((id, code), value) in _lastWrites)
        {
            if (!SetVcp(id, code, value, record: false).IsSuccess)
                failures++;
        }

        return failures == 0
            ? OperationResult<bool>.Success(true)
            : OperationResult<bool>.Failure(
                $"{failures} monitor setting(s) could not be re-applied.", ErrorCategory.ServiceUnavailable);
    }

    public bool HasSystemBattery()
    {
        if (NativeDisplay.GetSystemPowerStatus(out var status) == 0)
            return false;
        // BatteryFlag 128 = no system battery, 255 = unknown.
        return status.BatteryFlag is not (128 or 255);
    }

    private MonitorDevice ReadDevice(
        NativeDisplay.PHYSICAL_MONITOR physical, string deviceName, int index)
    {
        var id = $"{deviceName}|{index}";
        var name = ReadEdidName(deviceName, index)
            ?? (physical.szPhysicalMonitorDescription is { Length: > 0 } d
                ? d : deviceName.TrimStart('\\', '.'));

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
        // list and the vendor feature rows; cached after the first success.
        var cacheKey = $"{id}|{name}";
        IReadOnlyDictionary<int, IReadOnlyList<int>>? codeValues;
        IReadOnlyList<int>? knownVendorCodes = null;
        if (_capabilitiesCache.TryGetValue(cacheKey, out var cached))
        {
            codeValues = cached.CodeValues;
            knownVendorCodes = cached.AnsweringVendorCodes;
        }
        else
        {
            codeValues = ReadCodeValueGroups(physical.hPhysicalMonitor);
        }

        var vendorFeatures = codeValues is null
            ? []
            : BuildVendorFeatures(physical.hPhysicalMonitor, codeValues, knownVendorCodes);

        if (codeValues is not null && knownVendorCodes is null)
        {
            _capabilitiesCache[cacheKey] = new CachedCapabilities(
                codeValues, vendorFeatures.Select(f => f.Code).ToList());
        }

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
            InputSources = BuildInputSources(
                codeValues ?? new Dictionary<int, IReadOnlyList<int>>(), currentInput),
            VendorFeatures = vendorFeatures,
            PowerOffValue = codeValues is not null
                && codeValues.TryGetValue(VcpPowerMode, out var powerModes)
                ? VcpCapabilities.ChoosePowerOffValue(powerModes)
                : null,
            DdcError = codeValues is null
                ? "The monitor's feature list could not be read this time. Input and vendor controls are hidden; leave and reopen this page to retry."
                : null,
        };
    }

    /// <summary>
    /// The capabilities request is the flakiest DDC operation (monitors time
    /// out, replies arrive truncated), so it retries. Null means every attempt
    /// failed; the card tells the user instead of silently showing fewer
    /// controls.
    /// </summary>
    private static IReadOnlyDictionary<int, IReadOnlyList<int>>? ReadCodeValueGroups(nint handle)
    {
        for (var attempt = 0; attempt < 3; attempt++)
        {
            if (attempt > 0)
                Thread.Sleep(100);

            if (NativeDisplay.GetCapabilitiesStringLength(handle, out var length) == 0 || length <= 1)
                continue;

            var buffer = new byte[length];
            if (NativeDisplay.CapabilitiesRequestAndCapabilitiesReply(handle, buffer, length) == 0)
                continue;

            var capabilities = Encoding.ASCII.GetString(buffer).TrimEnd('\0');
            var map = VcpCapabilities.ParseCodeValueGroups(capabilities);
            if (map.Count > 0)
                return map;
        }

        return null;
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
    /// the feature rather than showing a control that cannot work. On a
    /// cached rescan only the codes that answered before are probed, so
    /// refusing codes pay their DDC timeout once per session.
    /// </summary>
    private static IReadOnlyList<VendorVcpFeature> BuildVendorFeatures(
        nint handle, IReadOnlyDictionary<int, IReadOnlyList<int>> codeValues,
        IReadOnlyList<int>? knownAnsweringCodes)
    {
        var features = new List<VendorVcpFeature>();
        foreach (var (code, values) in codeValues.OrderBy(p => p.Key))
        {
            if (code is < 0xE0 or > 0xFF || values.Count < 2)
                continue;

            if (knownAnsweringCodes is not null && !knownAnsweringCodes.Contains(code))
                continue;

            if (NativeDisplay.GetVCPFeatureAndVCPFeatureReply(handle, (byte)code, out _, out var current, out _) == 0)
                continue;

            var isNamed = VendorFeatureNames.TryGetValue(code, out var name);
            features.Add(new VendorVcpFeature(
                code,
                isNamed ? name! : $"Feature 0x{code:X2}",
                values.Distinct().Order().ToList(),
                (int)(current & 0xFF),
                isNamed,
                VendorFeatureHints.TryGetValue(code, out var hint) ? hint : null));
        }

        return features;
    }

    private OperationResult<bool> SetVcp(string monitorId, byte code, int value, bool record = true)
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

                    // Input source and power mode never enter the re-apply
                    // memory: pushing either back after a wake would switch
                    // inputs or turn the screen off under the user.
                    if (record && code is not (VcpInputSource or VcpPowerMode))
                        _lastWrites[(monitorId, code)] = value;

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

    /// <summary>
    /// The model name from the monitor's EDID, read via the PnP registry entry
    /// its device interface path points at. Null on any hiccup; callers fall
    /// back to the driver description ("Generic PnP Monitor").
    /// </summary>
    private static string? ReadEdidName(string adapterDeviceName, int monitorIndex)
    {
        try
        {
            var device = new NativeDisplay.DISPLAY_DEVICEW
            {
                cb = (uint)System.Runtime.InteropServices.Marshal.SizeOf<NativeDisplay.DISPLAY_DEVICEW>(),
            };
            if (NativeDisplay.EnumDisplayDevicesW(
                    adapterDeviceName, (uint)monitorIndex, ref device,
                    NativeDisplay.EDD_GET_DEVICE_INTERFACE_NAME) == 0)
            {
                return null;
            }

            // DeviceID: \\?\DISPLAY#GSM5B08#5&2e4cb92&0&UID4352#{guid}
            var parts = device.DeviceID.Split('#');
            if (parts.Length < 3)
                return null;

            using var key = Microsoft.Win32.Registry.LocalMachine.OpenSubKey(
                $@"SYSTEM\CurrentControlSet\Enum\DISPLAY\{parts[1]}\{parts[2]}\Device Parameters");
            return EdidParser.ParseMonitorName(key?.GetValue("EDID") as byte[]);
        }
        catch (Exception ex) when (ex is System.Security.SecurityException or UnauthorizedAccessException or IOException)
        {
            return null;
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
