using System.ComponentModel;
using System.Runtime.InteropServices;
using ThisIsMyPC.Core.Hardware.Sensors;
using ThisIsMyPC.Interop.Sensors.LibreHardwareMonitor;
using ThisIsMyPC.Interop.Win32.Security;

namespace ThisIsMyPC.Interop.Sensors;

/// <summary>
/// Read-only Windows memory/battery readings and a pinned LibreHardwareMonitor NvAPI port.
/// No drivers, dynamic code, bus access, or hardware control functions are used.
/// </summary>
public sealed partial class LibreHardwareSensorBackend : IHardwareSensorBackend
{
    private static readonly Lock NativeGate = new();
    private static nint _nvApiModule;
    private static nint _nvmlModule;
    private readonly Lock _gate = new();
    private Task<HardwareSensorSnapshot>? _read;
    private bool _disposed;

    public Task<HardwareSensorSnapshot> ReadAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            // Concurrent readers share one scan. Canceling a waiter never releases native serialization early.
            if (_read is null || _read.IsCompleted) _read = Task.Run(Read);
            return _read.WaitAsync(cancellationToken);
        }
    }

    private HardwareSensorSnapshot Read()
    {
        lock (NativeGate)
        {
            var readings = new List<HardwareSensorReading>();
            var notes = new List<string>
            {
                "Partial coverage: Windows memory and battery, plus supported NVIDIA sensors from LibreHardwareMonitor.",
                "CPU, motherboard, storage, AMD and Intel GPU sensors are not available yet. No sensor driver is installed or loaded.",
            };
            TryRead(() => ReadMemory(readings), "Memory", notes);
            TryRead(() => ReadBattery(readings, notes), "Battery", notes);
            TryRead(() => ReadNvidia(readings, notes), "NVIDIA", notes);
            return new(DateTimeOffset.UtcNow, readings.AsReadOnly(), notes.AsReadOnly());
        }
    }

    private static void TryRead(Action read, string device, List<string> notes)
    {
        try { read(); }
        catch (Exception ex) when (ex is Win32Exception or DllNotFoundException or EntryPointNotFoundException
                                   or BadImageFormatException or NotSupportedException or IOException)
        {
            notes.Add($"{device} readings unavailable: {ex.Message}");
        }
    }

    private static void ReadMemory(List<HardwareSensorReading> readings)
    {
        var state = new MemoryStatus { Length = (uint)Marshal.SizeOf<MemoryStatus>() };
        if (!GlobalMemoryStatusEx(ref state)) throw new Win32Exception(Marshal.GetLastPInvokeError());
        const double gib = 1024d * 1024 * 1024;
        readings.Add(new("memory/used", "memory", "Physical memory", HardwareSensorComponent.Memory,
            "Used", HardwareSensorUnit.Gigabytes, (state.TotalPhysical - state.AvailablePhysical) / gib));
        readings.Add(new("memory/available", "memory", "Physical memory", HardwareSensorComponent.Memory,
            "Available", HardwareSensorUnit.Gigabytes, state.AvailablePhysical / gib));
        readings.Add(new("memory/load", "memory", "Physical memory", HardwareSensorComponent.Memory,
            "Load", HardwareSensorUnit.Percent, state.Load));
        readings.Add(new("commit/used", "commit", "Committed memory", HardwareSensorComponent.Memory,
            "Used", HardwareSensorUnit.Gigabytes, (state.TotalPageFile - state.AvailablePageFile) / gib));
        readings.Add(new("commit/limit", "commit", "Committed memory", HardwareSensorComponent.Memory,
            "Limit", HardwareSensorUnit.Gigabytes, state.TotalPageFile / gib));
    }

    private static void ReadBattery(List<HardwareSensorReading> readings, List<string> notes)
    {
        if (!GetSystemPowerStatus(out var state)) throw new Win32Exception(Marshal.GetLastPInvokeError());
        if (state.BatteryFlag == 255) { notes.Add("Windows could not determine battery presence."); return; }
        if ((state.BatteryFlag & 128) != 0) { notes.Add("No system battery detected."); return; }
        readings.Add(new("battery/charge", "battery", "System battery", HardwareSensorComponent.Battery,
            "Charge", HardwareSensorUnit.Percent, state.Percent <= 100 ? state.Percent : null));
        readings.Add(new("battery/time", "battery", "System battery", HardwareSensorComponent.Battery,
            "Remaining time", HardwareSensorUnit.Hours, state.LifeTime == uint.MaxValue ? null : state.LifeTime / 3600d));
        notes.Add("Battery coverage is limited to Windows charge and estimated remaining time; health and charge rate are unavailable.");
    }

    private static void ReadNvidia(List<HardwareSensorReading> readings, List<string> notes)
    {
        if (_nvApiModule == 0)
        {
            var path = Path.Combine(Environment.SystemDirectory, "nvapi64.dll");
            if (!File.Exists(path)) { notes.Add("NVIDIA sensor library is not installed."); return; }
            var trust = AuthenticodeVerifier.VerifyTrusted(path, "NVIDIA Corporation");
            if (!trust.IsSuccess) { notes.Add("NVIDIA sensor library failed publisher verification."); return; }
            // The release app preloads this exact vendor image before CIG. Debug loads still use this verified path.
            _nvApiModule = NativeLibrary.Load(path);
        }
        if (!NvApi.IsAvailable) NvApi.Initialize();
        if (!NvApi.IsAvailable || NvApi.NvAPI_EnumPhysicalGPUs is null)
        { notes.Add("NVIDIA sensors are unavailable under the current driver or process restrictions."); return; }
        var handles = new NvApi.NvPhysicalGpuHandle[NvApi.MAX_PHYSICAL_GPUS];
        if (NvApi.NvAPI_EnumPhysicalGPUs(handles, out var count) != NvApi.NvStatus.OK || count < 0 || count > handles.Length)
        { notes.Add("NVIDIA GPU enumeration failed."); return; }
        var identities = new (uint Bus, uint Slot, bool Known)[count];
        for (var index = 0; index < count; index++)
        {
            if (NvApi.NvAPI_GPU_GetBusId is not null && NvApi.NvAPI_GPU_GetBusSlotId is not null
                && NvApi.NvAPI_GPU_GetBusId(handles[index], out var bus) == NvApi.NvStatus.OK
                && NvApi.NvAPI_GPU_GetBusSlotId(handles[index], out var slot) == NvApi.NvStatus.OK)
                identities[index] = (bus, slot, true);
        }
        using var power = OpenPowerReader(notes);
        for (var index = 0; index < count; index++)
        {
            var handle = handles[index];
            var id = "nvidia/" + index;
            var identity = identities[index];
            if (identity.Known)
                id = $"nvidia/bus/{identity.Bus}/slot/{identity.Slot}/adapter/{index}";
            var name = NvApi.NvAPI_GPU_GetFullName(handle, out var detected) == NvApi.NvStatus.OK ? detected : "NVIDIA GPU";
            TryRead(() => NvidiaSensorReader.Read(handle, id, name, readings), name, notes);
            if (power is not null && identity.Known && identities.Count(item => item == identity) == 1
                && power.ReadWatts(identity.Bus, identity.Slot) is { } watts)
                readings.Add(new(id + "/power", id, name, HardwareSensorComponent.Gpu,
                    "GPU power", HardwareSensorUnit.Watts, watts));
        }
        if (count == 0) notes.Add("No NVIDIA GPU detected.");
        notes.Add("NVIDIA fields appear only when the driver returns a supported reading. Power requires a unique PCI device match.");
    }

    private static NvmlReadOnly? OpenPowerReader(List<string> notes)
    {
        try
        {
            if (_nvmlModule == 0)
            {
                var path = Path.Combine(Environment.SystemDirectory, "nvml.dll");
                if (!File.Exists(path)) { notes.Add("NVIDIA power library is unavailable."); return null; }
                var trust = AuthenticodeVerifier.VerifyTrusted(path, "NVIDIA Corporation");
                if (!trust.IsSuccess)
                    trust = AuthenticodeVerifier.VerifyTrusted(path, "Microsoft Windows Hardware Compatibility Publisher");
                if (!trust.IsSuccess) { notes.Add("NVIDIA power library failed publisher verification."); return null; }
                _nvmlModule = NativeLibrary.Load(path);
            }
            var reader = NvmlReadOnly.Open(_nvmlModule);
            if (reader is null) notes.Add("NVIDIA power queries are unavailable with this driver.");
            return reader;
        }
        catch (Exception ex) when (ex is DllNotFoundException or BadImageFormatException or IOException)
        {
            notes.Add("NVIDIA power readings are unavailable under the current process restrictions.");
            return null;
        }
    }

    public void Dispose()
    {
        Task<HardwareSensorSnapshot>? pending;
        lock (_gate) { _disposed = true; pending = _read; }
        // No device handles are owned. The shared vendor library remains mapped for other app modules.
        // Join outstanding reads off the UI thread through the view model's asynchronous shutdown.
        if (pending is { IsCompleted: false }) pending.GetAwaiter().GetResult();
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MemoryStatus
    {
        public uint Length, Load;
        public ulong TotalPhysical, AvailablePhysical, TotalPageFile, AvailablePageFile;
        public ulong TotalVirtual, AvailableVirtual, AvailableExtendedVirtual;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct PowerStatus
    {
        public byte AcLine, BatteryFlag, Percent, Reserved;
        public uint LifeTime, FullLifeTime;
    }

    [LibraryImport("kernel32.dll", SetLastError = true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool GlobalMemoryStatusEx(ref MemoryStatus state);

    [LibraryImport("kernel32.dll", SetLastError = true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool GetSystemPowerStatus(out PowerStatus state);
}
