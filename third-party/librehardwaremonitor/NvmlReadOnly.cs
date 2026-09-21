// Derived from LibreHardwareMonitor Interop/NvidiaML.cs, MPL-2.0.
// Only NVML initialization, PCI identity lookup and power queries are retained.
using System.Runtime.InteropServices;

namespace ThisIsMyPC.Interop.Sensors.LibreHardwareMonitor;

internal sealed class NvmlReadOnly : IDisposable
{
    private readonly Shutdown _shutdown;
    private readonly GetHandle _getHandle;
    private readonly GetPower _getPower;
    private bool _disposed;

    private NvmlReadOnly(Shutdown shutdown, GetHandle getHandle, GetPower getPower)
    { _shutdown = shutdown; _getHandle = getHandle; _getPower = getPower; }

    internal static NvmlReadOnly? Open(nint verifiedModule)
    {
        if (!NativeLibrary.TryGetExport(verifiedModule, "nvmlInit_v2", out var init)
            || !NativeLibrary.TryGetExport(verifiedModule, "nvmlShutdown", out var close)
            || !NativeLibrary.TryGetExport(verifiedModule, "nvmlDeviceGetHandleByPciBusId_v2", out var device)
            || !NativeLibrary.TryGetExport(verifiedModule, "nvmlDeviceGetPowerUsage", out var power)) return null;
        var session = new NvmlReadOnly(Marshal.GetDelegateForFunctionPointer<Shutdown>(close),
            Marshal.GetDelegateForFunctionPointer<GetHandle>(device), Marshal.GetDelegateForFunctionPointer<GetPower>(power));
        return Marshal.GetDelegateForFunctionPointer<Initialize>(init)() == 0 ? session : null;
    }

    internal double? ReadWatts(uint bus, uint slot)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        // NVAPI has no domain field. Only the Windows domain-zero mapping is attempted.
        // The caller excludes duplicate bus/slot identities; there is no ordinal fallback.
        var pci = $"0000:{bus:X2}:{slot:X2}.0";
        if (_getHandle(pci, out var handle) != 0 || _getPower(handle, out var milliwatts) != 0) return null;
        return milliwatts / 1000d;
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _ = _shutdown();
    }

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate int Initialize();
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate int Shutdown();
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate int GetHandle([MarshalAs(UnmanagedType.LPStr)] string pciBusId, out nint device);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate int GetPower(nint device, out uint milliwatts);
}
