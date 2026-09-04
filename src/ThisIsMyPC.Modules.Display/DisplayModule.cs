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
        Description: "Monitor brightness, contrast, and input over DDC/CI. Changes apply immediately; the sliders are their own undo.",
        RequiredCapabilities: [SystemCapability.DdcCi],
        Group: ModuleGroup.Hardware,
        LoadOrder: 10);

    public Task<ModuleAvailability> CheckAvailabilityAsync()
    {
        // dxva2.dll ships with Windows; per-monitor support is a scan concern.
        return Task.FromResult(new ModuleAvailability(IsAvailable: true));
    }

    // The last scan, full or quick. A page open returns it at once and the
    // page refreshes in the background, so the DDC bus (seconds per monitor
    // for a capabilities request) never sits between a click and the page.
    private DisplayScanData? _snapshot;

    /// <summary>The most recent scan, if any; null until the first scan of the session.</summary>
    public DisplayScanData? Snapshot => _snapshot;

    /// <summary>
    /// What a page open gets: the snapshot when there is one, otherwise a
    /// quick scan (three DDC reads per monitor). Either way the page follows
    /// up with <see cref="RefreshAsync"/> for the full picture.
    /// </summary>
    public Task<OperationResult<object>> ScanSystemStateAsync()
    {
        if (_snapshot is { } snapshot)
            return Task.FromResult(OperationResult<object>.Success((object)snapshot));

        return Task.Run(() =>
        {
            var result = Scan(MonitorScanDepth.Quick);
            return result.IsSuccess
                ? OperationResult<object>.Success((object)result.Value!)
                : OperationResult<object>.Failure(result.ErrorMessage!, result.ErrorCategory ?? ErrorCategory.ServiceUnavailable, result.Exception);
        });
    }

    /// <summary>
    /// The full scan (capabilities strings, vendor probes) on a worker thread;
    /// its result becomes the snapshot the next page open shows instantly.
    /// </summary>
    public Task<OperationResult<DisplayScanData>> RefreshAsync() =>
        Task.Run(() => Scan(MonitorScanDepth.Full));

    /// <summary>Monitors changed (display change, resume): the next open scans afresh.</summary>
    public void InvalidateSnapshot() => _snapshot = null;

    private OperationResult<DisplayScanData> Scan(MonitorScanDepth depth)
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

            var ddc = _monitors.EnumerateMonitors(depth);
            if (ddc.IsSuccess)
                devices.AddRange(ddc.Value!);
            else
                scanError = ddc.ErrorMessage;

            var data = new DisplayScanData(devices, scanError, IsPartial: devices.Any(d => d.FeaturesPending));
            // A quick scan only fills an empty snapshot; it never replaces a
            // full one with less.
            if (depth == MonitorScanDepth.Full || _snapshot is null)
                _snapshot = data;
            return OperationResult<DisplayScanData>.Success(data);
        }
        catch (Exception ex)
        {
            return OperationResult<DisplayScanData>.Failure(
                $"Failed to scan displays: {ex.Message}", ErrorCategory.ServiceUnavailable, ex);
        }
    }

    public Task<OperationResult<bool>> ApplyChangeAsync(ChangeDescriptor change) =>
        Task.FromResult(OperationResult<bool>.Failure(
            "Display controls apply live and do not go through the change pipeline.",
            ErrorCategory.ServiceUnavailable));

    public Task<OperationResult<bool>> RevertChangeAsync(ChangeDescriptor change) =>
        ApplyChangeAsync(change);
}
