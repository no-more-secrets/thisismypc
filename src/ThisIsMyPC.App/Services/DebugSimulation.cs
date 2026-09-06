using ThisIsMyPC.Core.Modules;
using ThisIsMyPC.Core.Results;
using ThisIsMyPC.Core.Services;

namespace ThisIsMyPC.App.Services;

/// <summary>What the app is told to believe about the machine's shape.</summary>
public sealed record SimulatedDevice(string DisplayName, string Manufacturer, string Model, bool IsLaptop)
{
    public string SystemType => IsLaptop ? "Laptop (simulated)" : "Desktop (simulated)";
}

/// <summary>
/// In-memory overrides for what the app believes about this PC: Windows
/// edition, device shape, Owner Mode service state, and hardware ecosystems.
/// The Debug page sets them; nothing reads them from disk and nothing writes
/// them there, so a restart is a full reset. While any override is set the app
/// is in simulated mode: Apply, one-way actions, and service actions are
/// refused, so a pretend edition can never authorize a real write. Simulation
/// changes what the app shows and gates on; it never exercises real hardware
/// or Windows enforcement.
/// </summary>
public sealed class DebugSimulation
{
    public static readonly IReadOnlyList<SimulatedDevice> DevicePresets =
    [
        new("ASUS laptop", "ASUSTeK COMPUTER INC.", "ROG Zephyrus G14 (simulated)", IsLaptop: true),
        new("Lenovo laptop", "LENOVO", "ThinkPad X1 Carbon (simulated)", IsLaptop: true),
        new("HP laptop", "HP", "OMEN 16 (simulated)", IsLaptop: true),
        new("Dell desktop", "Dell Inc.", "XPS Desktop (simulated)", IsLaptop: false),
        new("Custom-built desktop", "To Be Filled By O.E.M.", "System Product Name (simulated)", IsLaptop: false),
    ];

    /// <summary>The ecosystems the Debug page can flip; the always-present subsystems are not overridable.</summary>
    public static readonly IReadOnlyList<SystemCapability> OverridableCapabilities =
    [
        SystemCapability.DdcCi,
        SystemCapability.HwInfo,
        SystemCapability.AsusAtkacpi,
        SystemCapability.OpenRgb,
    ];

    private readonly Dictionary<SystemCapability, bool> _capabilities = [];
    private readonly object _gate = new();
    private int _mutationsInFlight;
    private WindowsSku? _sku;
    private SimulatedDevice? _device;
    private OwnerModeState? _ownerModeState;

    /// <summary>Raised on the caller's thread after any override changes, a reset, or a refused change.</summary>
    public event EventHandler? Changed;

    /// <summary>The message every simulation change refused mid-mutation shows.</summary>
    public const string InFlightMessage =
        "A real change is running right now. Wait for it to finish, then set the simulation.";

    /// <summary>Why the last attempted change was refused; null after a change that went through.</summary>
    public string? LastRefusal { get; private set; }

    /// <summary>True while a real mutation (apply, undo, redo, Explorer restart, service action) holds a lease.</summary>
    public bool IsMutationInFlight
    {
        get { lock (_gate) return _mutationsInFlight > 0; }
    }

    /// <summary>
    /// The boundary between simulation and real writes. A mutation takes a
    /// lease before its first await and holds it through its rollback; while
    /// any lease is open no override can be set, so a batch can never switch
    /// to a simulated detector halfway. When simulation is already active the
    /// lease comes back refused and the caller writes nothing.
    /// </summary>
    public MutationLease BeginMutation()
    {
        lock (_gate)
        {
            if (IsActive)
                return MutationLease.Refused(BlockedMessage);
            _mutationsInFlight++;
            return new MutationLease(EndMutation);
        }
    }

    private void EndMutation()
    {
        lock (_gate)
        {
            _mutationsInFlight--;
        }
    }

    public WindowsSku? Sku
    {
        get => _sku;
        set => Set(ref _sku, value);
    }

    public SimulatedDevice? Device
    {
        get => _device;
        set => Set(ref _device, value);
    }

    public OwnerModeState? OwnerModeState
    {
        get => _ownerModeState;
        set => Set(ref _ownerModeState, value);
    }

    public bool? GetCapability(SystemCapability capability) =>
        _capabilities.TryGetValue(capability, out var value) ? value : null;

    public void SetCapability(SystemCapability capability, bool? available)
    {
        bool changed;
        lock (_gate)
        {
            if (Refuse())
            {
                Changed?.Invoke(this, EventArgs.Empty);
                return;
            }
            changed = available is null
                ? _capabilities.Remove(capability)
                : !_capabilities.TryGetValue(capability, out var current) || current != available.Value;
            if (available is not null)
                _capabilities[capability] = available.Value;
            if (changed)
                LastRefusal = null;
        }
        if (changed)
            Changed?.Invoke(this, EventArgs.Empty);
    }

    // Under the gate: a change while a real mutation holds a lease is refused
    // and reported; the caller raises Changed so bound dropdowns snap back.
    private bool Refuse()
    {
        if (_mutationsInFlight == 0)
            return false;
        LastRefusal = InFlightMessage;
        return true;
    }

    /// <summary>True while any override is set: the app is in simulated mode.</summary>
    public bool IsActive => _sku is not null || _device is not null || _ownerModeState is not null || _capabilities.Count > 0;

    /// <summary>One line naming every override, for the banner; empty when none.</summary>
    public string Summary
    {
        get
        {
            var parts = new List<string>();
            if (_sku is { } sku)
                parts.Add($"Windows 11 {SkuName(sku)}");
            if (_device is { } device)
                parts.Add(device.DisplayName);
            if (_ownerModeState is { } state)
                parts.Add($"Owner Mode service {OwnerModeStateName(state)}");
            foreach (var (capability, available) in _capabilities.OrderBy(p => p.Key))
                parts.Add($"{CapabilityName(capability)} {(available ? "present" : "missing")}");
            return string.Join(", ", parts);
        }
    }

    /// <summary>The message every refused action shows.</summary>
    public const string BlockedMessage =
        "Simulated mode is on, so nothing was changed. Click Reset on the simulation bar or the Debug page first.";

    public void Reset()
    {
        lock (_gate)
        {
            // A lease is only ever taken while inactive, so there is nothing to
            // reset during a mutation; the refusal path is for completeness.
            if (Refuse())
            {
                Changed?.Invoke(this, EventArgs.Empty);
                return;
            }
            if (!IsActive)
                return;
            _sku = null;
            _device = null;
            _ownerModeState = null;
            _capabilities.Clear();
            LastRefusal = null;
        }
        Changed?.Invoke(this, EventArgs.Empty);
    }

    private void Set<T>(ref T field, T value)
    {
        lock (_gate)
        {
            if (EqualityComparer<T>.Default.Equals(field, value))
                return;
            if (Refuse())
            {
                Changed?.Invoke(this, EventArgs.Empty);
                return;
            }
            field = value;
            LastRefusal = null;
        }
        Changed?.Invoke(this, EventArgs.Empty);
    }

    public static string SkuName(WindowsSku sku) => sku switch
    {
        WindowsSku.Home => "Home",
        WindowsSku.Pro => "Pro",
        WindowsSku.Enterprise => "Enterprise",
        WindowsSku.Education => "Education",
        _ => sku.ToString(),
    };

    public static string OwnerModeStateName(OwnerModeState state) => state switch
    {
        Services.OwnerModeState.Running => "running",
        Services.OwnerModeState.Stopped => "stopped",
        Services.OwnerModeState.Disabled => "disabled",
        Services.OwnerModeState.NotInstalled => "not installed",
        _ => "unknown",
    };

    public static string CapabilityName(SystemCapability capability) => capability switch
    {
        SystemCapability.DdcCi => "DDC/CI monitors",
        SystemCapability.HwInfo => "HWiNFO",
        SystemCapability.AsusAtkacpi => "ASUS ATKACPI",
        SystemCapability.OpenRgb => "OpenRGB",
        _ => capability.ToString(),
    };
}

/// <summary>
/// A real mutation's hold on the simulation gate. <see cref="Refusal"/> is
/// non-null when the mutation must not run (simulation is active); otherwise
/// overrides are locked out until the lease is disposed. <see cref="Open"/> is
/// the no-simulation case: never refused, locks nothing.
/// </summary>
public sealed class MutationLease : IDisposable
{
    private Action? _release;

    internal MutationLease(Action? release) => _release = release;

    /// <summary>The message to show instead of mutating; null when the mutation may proceed.</summary>
    public string? Refusal { get; private init; }

    public bool IsRefused => Refusal is not null;

    public static MutationLease Open() => new(null);

    internal static MutationLease Refused(string message) => new(null) { Refusal = message };

    public void Dispose()
    {
        var release = Interlocked.Exchange(ref _release, null);
        release?.Invoke();
    }
}

/// <summary>
/// The app's capability detector with the Debug page's overrides laid over the
/// real one. Pass-through while nothing is simulated. The real detector is
/// never modified and never learns about the overrides, so resetting is exact.
/// </summary>
public sealed class SimulatedCapabilityDetector : ICapabilityDetector
{
    private readonly ICapabilityDetector _real;
    private readonly DebugSimulation _simulation;

    public SimulatedCapabilityDetector(ICapabilityDetector real, DebugSimulation simulation)
    {
        ArgumentNullException.ThrowIfNull(real);
        ArgumentNullException.ThrowIfNull(simulation);
        _real = real;
        _simulation = simulation;
    }

    public WindowsSku? Sku => _simulation.Sku ?? _real.Sku;

    public string? SkuDetectionFailureReason => _simulation.Sku is null ? _real.SkuDetectionFailureReason : null;

    public bool IsSkuRestricted(WindowsSku? restriction) =>
        restriction is { } required && Sku is { } current && current.Tier() < required.Tier();

    public bool IsOwnerModeAvailable => _simulation.OwnerModeState is { } state
        ? state == OwnerModeState.Running
        : _real.IsOwnerModeAvailable;

    public bool IsAvailable(SystemCapability capability) => GetAvailability(capability).IsAvailable;

    public ModuleAvailability GetAvailability(SystemCapability capability) =>
        _simulation.GetCapability(capability) switch
        {
            true => new ModuleAvailability(true, null, "Simulated as present on the Debug page."),
            false => new ModuleAvailability(false, $"{DebugSimulation.CapabilityName(capability)} simulated as missing.", "Reset the simulation on the Debug page."),
            null => _real.GetAvailability(capability),
        };

    public IReadOnlyList<CapabilityReportRow> GetCapabilityReport() =>
        _real.GetCapabilityReport()
            .Select(row => new CapabilityReportRow(row.Capability, row.DisplayName, GetAvailability(row.Capability)))
            .ToList();
}

/// <summary>
/// The Owner Mode service control with the Debug page's state laid over it.
/// Reports the simulated state and refuses to enable or disable the real
/// service while any simulation is set; otherwise passes straight through.
/// </summary>
public sealed class SimulatedOwnerModeControl : IOwnerModeServiceControl
{
    private readonly IOwnerModeServiceControl _real;
    private readonly DebugSimulation _simulation;

    public SimulatedOwnerModeControl(IOwnerModeServiceControl real, DebugSimulation simulation)
    {
        ArgumentNullException.ThrowIfNull(real);
        ArgumentNullException.ThrowIfNull(simulation);
        _real = real;
        _simulation = simulation;
        _real.StateChanged += (s, e) => StateChanged?.Invoke(this, e);
        _simulation.Changed += (s, e) => StateChanged?.Invoke(this, e);
    }

    public event EventHandler? StateChanged;

    public OwnerModeState GetState() => _simulation.OwnerModeState ?? _real.GetState();

    public async Task<OperationResult<bool>> EnableAsync(CancellationToken cancellationToken = default)
    {
        using var lease = _simulation.BeginMutation();
        if (lease.Refusal is { } refusal)
            return OperationResult<bool>.Failure(refusal, ErrorCategory.ServiceUnavailable);
        return await _real.EnableAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task<OperationResult<bool>> DisableAsync(CancellationToken cancellationToken = default)
    {
        using var lease = _simulation.BeginMutation();
        if (lease.Refusal is { } refusal)
            return OperationResult<bool>.Failure(refusal, ErrorCategory.ServiceUnavailable);
        return await _real.DisableAsync(cancellationToken).ConfigureAwait(false);
    }
}
