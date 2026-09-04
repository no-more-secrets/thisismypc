using ThisIsMyPC.Core.Display;
using ThisIsMyPC.Core.Results;
using ThisIsMyPC.Core.Services;

namespace ThisIsMyPC.Modules.Display.Tests.Fakes;

/// <summary>Scriptable IMonitorService: seed Devices, flip HasBattery, inject failures.</summary>
public sealed class FakeMonitorService : IMonitorService
{
    public List<MonitorDevice> Devices { get; } = [];
    public bool HasBattery { get; set; }
    public string? EnumerateFailure { get; set; }
    public List<string> Calls { get; } = [];

    /// <summary>Devices a quick scan returns when set; otherwise Devices with FeaturesPending on each.</summary>
    public List<MonitorDevice>? QuickDevices { get; set; }

    public OperationResult<IReadOnlyList<MonitorDevice>> EnumerateMonitors(MonitorScanDepth depth = MonitorScanDepth.Full)
    {
        Calls.Add($"EnumerateMonitors:{depth}");
        if (EnumerateFailure is { } error)
            return OperationResult<IReadOnlyList<MonitorDevice>>.Failure(error, ErrorCategory.ServiceUnavailable);
        if (depth == MonitorScanDepth.Full)
            return OperationResult<IReadOnlyList<MonitorDevice>>.Success(Devices.ToList());
        return OperationResult<IReadOnlyList<MonitorDevice>>.Success(
            QuickDevices?.ToList()
            ?? Devices.Select(d => d with { InputSources = [], VendorFeatures = [], FeaturesPending = true }).ToList());
    }

    public OperationResult<bool> SetBrightness(string monitorId, int value)
    {
        Calls.Add($"SetBrightness:{monitorId}={value}");
        return OperationResult<bool>.Success(true);
    }

    public OperationResult<bool> SetContrast(string monitorId, int value)
    {
        Calls.Add($"SetContrast:{monitorId}={value}");
        return OperationResult<bool>.Success(true);
    }

    public OperationResult<bool> SetInputSource(string monitorId, int value)
    {
        Calls.Add($"SetInputSource:{monitorId}={value}");
        return OperationResult<bool>.Success(true);
    }

    public OperationResult<bool> SetVcpValue(string monitorId, int vcpCode, int value)
    {
        Calls.Add($"SetVcpValue:{monitorId}:0x{vcpCode:X2}={value}");
        return OperationResult<bool>.Success(true);
    }

    public OperationResult<bool> ReapplyLastWrites()
    {
        Calls.Add("ReapplyLastWrites");
        return OperationResult<bool>.Success(true);
    }

    public bool HasSystemBattery() => HasBattery;
}
