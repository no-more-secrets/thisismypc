using ThisIsMyPC.Core.Display;
using ThisIsMyPC.Core.Results;

namespace ThisIsMyPC.Core.Services;

/// <summary>
/// DDC/CI access to external monitors via dxva2.dll. Implementations resolve
/// physical monitor handles fresh per call (handles go stale across display
/// changes and sleep), so a failed set after replugging is fixed by rescanning.
/// </summary>
public interface IMonitorService
{
    /// <summary>
    /// Enumerates physical monitors and reads their DDC state (brightness,
    /// contrast, input list from the capabilities string). The capabilities
    /// request is slow (hundreds of ms per monitor); call from a scan, not a
    /// hot path.
    /// </summary>
    OperationResult<IReadOnlyList<MonitorDevice>> EnumerateMonitors();

    OperationResult<bool> SetBrightness(string monitorId, int value);

    OperationResult<bool> SetContrast(string monitorId, int value);

    /// <summary>Sets VCP 0x60. The monitor may go dark if the target input has no signal.</summary>
    OperationResult<bool> SetInputSource(string monitorId, int value);

    /// <summary>Sets an arbitrary VCP code (vendor features like a blue light filter).</summary>
    OperationResult<bool> SetVcpValue(string monitorId, int vcpCode, int value);

    /// <summary>
    /// Re-pushes this session's successful writes (brightness, contrast,
    /// vendor features; never input source). Called after resume or a display
    /// change, when monitors may have reset their DDC state.
    /// </summary>
    OperationResult<bool> ReapplyLastWrites();

    /// <summary>True when the machine has a system battery (laptop heuristic for the internal panel).</summary>
    bool HasSystemBattery();
}
