using ThisIsMyPC.Core.Changes;
using ThisIsMyPC.Core.Display;
using ThisIsMyPC.Core.Modules;
using ThisIsMyPC.Core.Results;
using ThisIsMyPC.Core.Services;
using ThisIsMyPC.Modules.Display.Models;
using ThisIsMyPC.Modules.Display.Services;

namespace ThisIsMyPC.Modules.Display;

/// <summary>
/// Monitor control over DDC/CI plus the laptop panel via the power plan.
///
/// Deliberate carve-out from the pending-changes pipeline: brightness,
/// contrast, and input are ephemeral hardware state, not system configuration.
/// A slider is its own undo, nothing is written to the registry or persisted
/// by Windows against a profile, and staging "brightness 40 to 70" for a later
/// Apply would make the control unusable. The view model talks to the services
/// directly; ApplyChangeAsync exists only to satisfy IModule.
/// </summary>
public sealed class DisplayModule : IModule
{
    private readonly IMonitorService _monitors;
    private readonly IPowerService _power;

    public DisplayModule(IMonitorService monitors, IPowerService power)
    {
        _monitors = monitors;
        _power = power;
    }

    public ModuleInfo Info { get; } = new(
        Name: "Display",
        Icon: "display",
        Description: "Monitor brightness, contrast, and input over DDC/CI",
        RequiredCapabilities: [SystemCapability.DdcCi],
        Group: ModuleGroup.Hardware,
        LoadOrder: 10);

    public Task<ModuleAvailability> CheckAvailabilityAsync()
    {
        // dxva2.dll ships with Windows; per-monitor support is a scan concern.
        return Task.FromResult(new ModuleAvailability(IsAvailable: true));
    }

    public Task<OperationResult<object>> ScanSystemStateAsync()
    {
        return Task.Run(() =>
        {
            try
            {
                var devices = new List<MonitorDevice>();
                string? scanError = null;

                // The internal panel first, so it tops the list on laptops.
                if (_monitors.HasSystemBattery()
                    && new InternalPanelService(_power).ReadPanel() is { } panel)
                {
                    devices.Add(panel);
                }

                var ddc = _monitors.EnumerateMonitors();
                if (ddc.IsSuccess)
                    devices.AddRange(ddc.Value!);
                else
                    scanError = ddc.ErrorMessage;

                return OperationResult<object>.Success(
                    (object)new DisplayScanData(devices, scanError));
            }
            catch (Exception ex)
            {
                return OperationResult<object>.Failure(
                    $"Failed to scan displays: {ex.Message}", ErrorCategory.ServiceUnavailable, ex);
            }
        });
    }

    public Task<OperationResult<bool>> ApplyChangeAsync(ChangeDescriptor change) =>
        Task.FromResult(OperationResult<bool>.Failure(
            "Display controls apply live and do not go through the change pipeline.",
            ErrorCategory.ServiceUnavailable));

    public Task<OperationResult<bool>> RevertChangeAsync(ChangeDescriptor change) =>
        ApplyChangeAsync(change);
}
