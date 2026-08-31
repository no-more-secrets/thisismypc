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

    public OperationResult<IReadOnlyList<MonitorDevice>> EnumerateMonitors()
    {
        Calls.Add("EnumerateMonitors");
        return EnumerateFailure is { } error
            ? OperationResult<IReadOnlyList<MonitorDevice>>.Failure(error, ErrorCategory.ServiceUnavailable)
            : OperationResult<IReadOnlyList<MonitorDevice>>.Success(Devices.ToList());
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

    public bool HasSystemBattery() => HasBattery;
}
