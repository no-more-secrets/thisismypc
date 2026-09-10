using ThisIsMyPC.Core.Services;

namespace ThisIsMyPC.Core.Hardware.Detection;

/// <summary>
/// The module-level facts behind the Hardware tabs. Starts from the shared
/// <see cref="IHardwareDetectionService"/> inventory (identity, firmware
/// chassis types, platform role, battery, ATKACPI) and adds what that
/// inventory leaves unobserved on purpose: every companion with a confirmed
/// absence or a launch path and observed ownership, the OpenRGB SDK probe,
/// and the internal display panel. Pure orchestration: every native call sits
/// behind an interface, so this runs unchanged under a scripted machine in
/// tests. One pass at a time; concurrent callers share the pass in flight.
/// </summary>
public sealed class HardwareFactsProvider : IHardwareFactsProvider, IDisposable
{
    private readonly IHardwareDetectionService _inventory;
    private readonly IHardwareProbeEnvironment _environment;
    private readonly Func<bool?>? _internalPanelProbe;
    private readonly CompanionDetector _companions;
    private readonly TimeSpan _openRgbTimeout;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private HardwareDetectionSnapshot? _current;

    public HardwareFactsProvider(
        IHardwareDetectionService inventory,
        IRegistryService registry,
        IHardwareProbeEnvironment environment,
        IScheduledTaskService? tasks = null,
        IServiceControlService? services = null,
        Func<bool?>? internalPanelProbe = null,
        TimeSpan? openRgbTimeout = null)
    {
        ArgumentNullException.ThrowIfNull(inventory);
        ArgumentNullException.ThrowIfNull(registry);
        ArgumentNullException.ThrowIfNull(environment);
        _inventory = inventory;
        _environment = environment;
        _internalPanelProbe = internalPanelProbe;
        _companions = new CompanionDetector(registry, environment, tasks, services);
        _openRgbTimeout = openRgbTimeout ?? TimeSpan.FromMilliseconds(750);
    }

    public HardwareDetectionSnapshot? Current => _current;

    public event EventHandler? Changed;

    public async Task<HardwareDetectionSnapshot> GetAsync(bool refresh = false, CancellationToken cancellationToken = default)
    {
        if (!refresh && _current is { } cached)
            return cached;

        HardwareDetectionSnapshot snapshot;
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (!refresh && _current is { } raced)
                return raced;

            var notes = new List<string>();
            var inventory = await ReadInventoryAsync(refresh, notes, cancellationToken).ConfigureAwait(false);
            snapshot = await Task.Run(() => Detect(inventory, notes), cancellationToken).ConfigureAwait(false);
            _current = snapshot;
        }
        finally
        {
            _gate.Release();
        }

        Changed?.Invoke(this, EventArgs.Empty);
        return snapshot;
    }

    /// <summary>
    /// A failed inventory read is a missing set of facts, not a failed page:
    /// the tabs then read Unknown with the reason in their details.
    /// </summary>
    private async Task<HardwareSnapshot> ReadInventoryAsync(bool refresh, List<string> notes, CancellationToken cancellationToken)
    {
        try
        {
            return refresh
                ? await _inventory.RefreshAsync(cancellationToken).ConfigureAwait(false)
                : await _inventory.GetSnapshotAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
#pragma warning disable CA1031 // Any inventory failure degrades to "not observed".
        catch (Exception ex)
        {
            notes.Add($"Shared hardware inventory: read failed ({ex.GetType().Name}).");
            return new HardwareSnapshot { ObservedAt = DateTimeOffset.UtcNow };
        }
#pragma warning restore CA1031
    }

    /// <summary>One synchronous module-level pass over a shared inventory snapshot. Public for tests and diagnostics.</summary>
    public HardwareDetectionSnapshot Detect(HardwareSnapshot inventory, List<string>? notes = null)
    {
        ArgumentNullException.ThrowIfNull(inventory);
        notes ??= [];
        notes.AddRange(inventory.Issues.Select(issue => "Shared hardware inventory: " + issue));

        var shared = inventory.Facts;
        var chassis = shared.FormFactor.SmbiosChassisTypes;
        notes.Add(chassis is null
            ? "SMBIOS chassis types: not read."
            : $"SMBIOS chassis types: {(chassis.Count == 0 ? "none" : string.Join(", ", chassis))}.");

        var panel = _internalPanelProbe is null ? null : Safe(_internalPanelProbe, "internal panel", notes);

        // The SDK probe only matters while OpenRGB runs; a refused connection
        // is instant, so probing costs nothing when it does not.
        OpenRgbProbeResult? openRgb = null;
        if (_environment.RunningProcessPath("OpenRGB") is not null)
        {
            openRgb = Safe(() => _environment.ProbeOpenRgbServer(OpenRgbSdkProtocol.DefaultPort, _openRgbTimeout), "OpenRGB server probe", notes)
                ?? OpenRgbProbeResult.Unreachable("probe failed");
        }

        var detections = _companions.DetectAll(shared.AsusPlatformDriverPresent, openRgb);
        var launchPaths = new Dictionary<CompanionApp, string>();
        var companions = new List<CompanionObservation>();
        foreach (var detection in detections)
        {
            notes.AddRange(detection.Notes);
            if (detection.LaunchPath is { Length: > 0 } path)
                launchPaths[detection.Observation.App] = path;
            companions.Add(MergeSharedSighting(detection.Observation, shared.Companion(detection.Observation.App), notes));
        }

        var facts = shared with
        {
            FormFactor = shared.FormFactor with { HasInternalDisplayPanel = panel },
            Companions = companions,
            OpenRgbServerReachable = openRgb?.Reachable,
            OpenRgbDeviceCount = openRgb is { Reachable: true } ? openRgb.DeviceCount : null,
        };

        return new HardwareDetectionSnapshot(facts, inventory, launchPaths, notes, DateTimeOffset.Now);
    }

    /// <summary>
    /// The shared inventory records only positive sightings (a registered
    /// name, a process name). Where it saw a companion this layer did not,
    /// the sighting is kept; ownership and the launch path stay this layer's.
    /// </summary>
    private static CompanionObservation MergeSharedSighting(CompanionObservation own, CompanionObservation? shared, List<string> notes)
    {
        if (shared is null || (own.IsInstalled || !shared.IsInstalled) && (own.IsRunning || !shared.IsRunning))
            return own;

        notes.Add($"{CompanionNames.Of(own.App)}: the shared inventory saw it {(shared.IsRunning ? "running" : "registered")}.");
        return new CompanionObservation(own.App, own.IsInstalled || shared.IsInstalled, own.IsRunning || shared.IsRunning, own.ObservedOwnership);
    }

    public void Dispose() => _gate.Dispose();

    private static T? Safe<T>(Func<T?> probe, string what, List<string> notes)
    {
        try
        {
            return probe();
        }
#pragma warning disable CA1031 // A failed probe is a missing fact, never a failed page.
        catch (Exception ex)
        {
            notes.Add($"{what}: probe failed ({ex.GetType().Name}).");
            return default;
        }
#pragma warning restore CA1031
    }
}
