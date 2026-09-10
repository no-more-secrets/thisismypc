using ThisIsMyPC.Core.Results;

namespace ThisIsMyPC.Core.Hardware.Lighting;

/// <summary>
/// One open connection to a set of lighting devices, whoever drives them (the
/// built-in controllers, or an OpenRGB SDK server). Reads enumerate devices;
/// writes change device state live, the same carve-out from the pending
/// pipeline the Display module documents: a color is its own undo and
/// nothing is persisted by Windows. The tab still refuses every write unless
/// the compatibility policy granted WriteDevices.
/// </summary>
public interface ILightingSession : IDisposable
{
    /// <summary>False once the transport failed; the owner drops the session and opens another.</summary>
    bool IsConnected { get; }

    /// <summary>Raised when the device list changed behind the session; callers re-enumerate.</summary>
    event EventHandler? DeviceListChanged;

    Task<OperationResult<IReadOnlyList<LightingDevice>>> GetDevicesAsync(CancellationToken cancellationToken = default);

    /// <summary>Re-reads one device (after a mode change the device may have changed its colors).</summary>
    Task<OperationResult<LightingDevice>> GetDeviceAsync(int deviceIndex, CancellationToken cancellationToken = default);

    /// <summary>Switches the device to <paramref name="mode"/> with the settings the mode carries.</summary>
    Task<OperationResult<bool>> SetModeAsync(int deviceIndex, LightingMode mode, CancellationToken cancellationToken = default);

    /// <summary>Writes every LED of the device.</summary>
    Task<OperationResult<bool>> SetLedsAsync(int deviceIndex, IReadOnlyList<RgbColor> colors, CancellationToken cancellationToken = default);

    /// <summary>Writes every LED of one zone.</summary>
    Task<OperationResult<bool>> SetZoneLedsAsync(int deviceIndex, int zoneIndex, IReadOnlyList<RgbColor> colors, CancellationToken cancellationToken = default);

    /// <summary>Asks the device to keep its current mode across power cycles (modes with ManualSave only).</summary>
    Task<OperationResult<bool>> SaveModeAsync(int deviceIndex, LightingMode mode, CancellationToken cancellationToken = default);
}

/// <summary>One device a backend found, for the compatibility policy and the evidence list.</summary>
/// <param name="Name">Product name as the controller reports it.</param>
/// <param name="Controller">The built-in controller family that drives it (for example "ENE SMBus").</param>
/// <param name="Location">Where it was found: the HID path or the bus and address.</param>
public sealed record LightingDeviceSummary(string Name, LightingDeviceType Type, string Controller, string Location);

/// <summary>What one detection pass found, and what it could not check.</summary>
public sealed record LightingInventory(
    IReadOnlyList<LightingDeviceSummary> Devices,
    IReadOnlyList<string> Notes,
    DateTimeOffset ObservedAt)
{
    public static LightingInventory Empty { get; } = new([], [], DateTimeOffset.MinValue);
}

/// <summary>
/// Owns the lighting devices for the life of the app. Detection runs once
/// and is cached; a rescan re-enumerates and tells open sessions the list
/// changed. Sessions are views over the same detected controllers, so
/// opening one never re-probes the buses.
/// </summary>
public interface ILightingBackend
{
    /// <summary>Detects supported devices without opening a session. Cached unless <paramref name="rescan"/>.</summary>
    Task<OperationResult<LightingInventory>> DetectAsync(bool rescan = false, CancellationToken cancellationToken = default);

    Task<OperationResult<ILightingSession>> OpenAsync(CancellationToken cancellationToken = default);
}
