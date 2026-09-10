using ThisIsMyPC.Core.Changes;
using ThisIsMyPC.Core.Hardware;
using ThisIsMyPC.Core.Modules;
using ThisIsMyPC.Core.Results;
using ThisIsMyPC.Core.Settings;
using ThisIsMyPC.Modules.Hardware.Models;

namespace ThisIsMyPC.Modules.Hardware;

/// <summary>
/// One Hardware tab decided by <see cref="HardwareCompatibilityPolicy"/> over
/// the shared detection snapshot. The tab is always available in the sidebar;
/// what it can do here is the decision it renders. Companion install and open
/// are the tab's only actions in this batch and they run through the App's
/// action service, never here. Nothing goes through the change pipeline:
/// these tabs write no system configuration, and the one that will write
/// devices (Lighting, through OpenRGB) writes ephemeral hardware state that is
/// its own undo, the same carve-out the Display module documents.
/// </summary>
public abstract class HardwareCompanionModule : IModule
{
    private readonly IHardwareFactsProvider _facts;
    private readonly ISettingsService? _settings;

    protected HardwareCompanionModule(IHardwareFactsProvider facts, ISettingsService? settings, ModuleInfo info, HardwareDomain domain)
    {
        ArgumentNullException.ThrowIfNull(facts);
        ArgumentNullException.ThrowIfNull(info);
        _facts = facts;
        _settings = settings;
        Info = info;
        Domain = domain;
    }

    public ModuleInfo Info { get; }

    public HardwareDomain Domain { get; }

    /// <summary>Every Hardware tab stays reachable; the page explains what it can do on this PC.</summary>
    public Task<ModuleAvailability> CheckAvailabilityAsync() =>
        Task.FromResult(new ModuleAvailability(IsAvailable: true));

    /// <summary>A cached snapshot older than this is re-checked behind the page after it opens.</summary>
    public static TimeSpan BackgroundRefreshAge { get; } = TimeSpan.FromSeconds(30);

    private bool _forceNextScan;

    /// <summary>The page's Refresh button: the next open runs a fresh pass instead of showing the cache.</summary>
    public void InvalidateSnapshot() => _forceNextScan = true;

    /// <summary>
    /// The cached snapshot when there is one (instant open), else a first
    /// detection pass. After <see cref="InvalidateSnapshot"/> the pass is
    /// fresh. The result says whether the page should re-check behind itself.
    /// </summary>
    public async Task<OperationResult<object>> ScanSystemStateAsync()
    {
        var force = _forceNextScan;
        _forceNextScan = false;
        var cached = force ? null : _facts.Current;
        var result = await EvaluateAsync(refresh: force).ConfigureAwait(false);
        if (!result.IsSuccess)
            return OperationResult<object>.Failure(result.ErrorMessage!, result.ErrorCategory ?? ErrorCategory.ServiceUnavailable, result.Exception);

        var stale = cached is not null && DateTimeOffset.Now - cached.ObservedAt > BackgroundRefreshAge;
        return OperationResult<object>.Success(result.Value! with { RefreshInBackground = stale });
    }

    /// <summary>A fresh detection pass, for the page's background re-check.</summary>
    public Task<OperationResult<HardwareTabScanData>> RefreshAsync() => EvaluateAsync(refresh: true);

    /// <summary>Decides this tab from a snapshot without touching the machine. Public for tests and the App's page.</summary>
    public HardwareTabScanData Evaluate(HardwareDetectionSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        var options = new HardwareCompatibilityOptions(
            ShowAllControls: _settings?.GetAppBool(AppSettingKeys.ShowAllHardwareControls, false) ?? false);
        var report = HardwareCompatibilityPolicy.Decide(snapshot.Facts, options);
        var decision = report.For(Domain);
        var launchPath = decision.Action is { } action ? snapshot.LaunchPathOf(action.App) : null;
        return new HardwareTabScanData(decision, report, launchPath, snapshot.Notes, snapshot.ObservedAt);
    }

    private async Task<OperationResult<HardwareTabScanData>> EvaluateAsync(bool refresh)
    {
        try
        {
            var snapshot = await _facts.GetAsync(refresh).ConfigureAwait(false);
            return OperationResult<HardwareTabScanData>.Success(Evaluate(snapshot));
        }
#pragma warning disable CA1031 // Detection failures become a page message, never a crash.
        catch (Exception ex)
        {
            return OperationResult<HardwareTabScanData>.Failure(
                $"Hardware detection failed: {ex.Message}", ErrorCategory.ServiceUnavailable, ex);
        }
#pragma warning restore CA1031
    }

    public Task<OperationResult<bool>> ApplyChangeAsync(ChangeDescriptor change) =>
        Task.FromResult(OperationResult<bool>.Failure(
            $"{Info.Name} has no staged changes; companion actions run directly.",
            ErrorCategory.ServiceUnavailable));

    public Task<OperationResult<bool>> RevertChangeAsync(ChangeDescriptor change) => ApplyChangeAsync(change);
}

/// <summary>Laptop performance modes, battery limit and keyboard controls through G-Helper.</summary>
public sealed class SystemControlModule : HardwareCompanionModule
{
    public const string ModuleName = "System Control";

    public SystemControlModule(IHardwareFactsProvider facts, ISettingsService? settings = null)
        : base(facts, settings, new ModuleInfo(
            Name: ModuleName,
            Icon: "system-control",
            Description: "Performance modes, battery limit and keyboard controls on supported laptops, through G-Helper.",
            RequiredCapabilities: [],
            Group: ModuleGroup.Hardware,
            LoadOrder: 20), HardwareDomain.SystemControl)
    {
    }
}

/// <summary>RGB lighting through the OpenRGB SDK server.</summary>
public sealed class LightingModule : HardwareCompanionModule
{
    public const string ModuleName = "Lighting";

    public LightingModule(IHardwareFactsProvider facts, ISettingsService? settings = null)
        : base(facts, settings, new ModuleInfo(
            Name: ModuleName,
            Icon: "lighting",
            Description: "RGB device colors, brightness and modes through OpenRGB.",
            RequiredCapabilities: [],
            Group: ModuleGroup.Hardware,
            LoadOrder: 30), HardwareDomain.Lighting)
    {
    }
}

/// <summary>Fan curves through FanControl.</summary>
public sealed class CoolingModule : HardwareCompanionModule
{
    public const string ModuleName = "Cooling";

    public CoolingModule(IHardwareFactsProvider facts, ISettingsService? settings = null)
        : base(facts, settings, new ModuleInfo(
            Name: ModuleName,
            Icon: "cooling",
            Description: "Fan curves and cooling presets through FanControl.",
            RequiredCapabilities: [],
            Group: ModuleGroup.Hardware,
            LoadOrder: 40), HardwareDomain.Cooling)
    {
    }
}

/// <summary>Curated sensor readout through LibreHardwareMonitor.</summary>
public sealed class MonitoringModule : HardwareCompanionModule
{
    public const string ModuleName = "Monitoring";

    public MonitoringModule(IHardwareFactsProvider facts, ISettingsService? settings = null)
        : base(facts, settings, new ModuleInfo(
            Name: ModuleName,
            Icon: "monitoring",
            Description: "Temperatures, load, clocks, power and fan speeds with short history.",
            RequiredCapabilities: [],
            Group: ModuleGroup.Hardware,
            LoadOrder: 50), HardwareDomain.Monitoring)
    {
    }
}
