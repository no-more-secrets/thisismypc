using ThisIsMyPC.Core.Results;

namespace ThisIsMyPC.Core.Hardware.Lighting;

/// <summary>
/// One open connection to an OpenRGB SDK server. Reads enumerate devices;
/// writes change device state live, the same carve-out from the pending
/// pipeline the Display module documents: a color is its own undo and
/// nothing is persisted by Windows. The tab still refuses every write unless
/// the compatibility policy granted WriteDevices.
/// </summary>
public interface IOpenRgbSession : IDisposable
{
    /// <summary>The version both ends agreed on.</summary>
    uint ProtocolVersion { get; }

    /// <summary>False once the socket failed; the owner drops the session and connects again.</summary>
    bool IsConnected { get; }

    /// <summary>Raised when the server announced a device list change; callers re-enumerate.</summary>
    event EventHandler? DeviceListChanged;

    Task<OperationResult<IReadOnlyList<LightingDevice>>> GetDevicesAsync(CancellationToken cancellationToken = default);

    /// <summary>Re-reads one device (after a mode change the server may have changed its colors).</summary>
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

/// <summary>Opens SDK sessions. The Win32 layer implements it over TCP; tests script it.</summary>
public interface IOpenRgbClient
{
    Task<OperationResult<IOpenRgbSession>> ConnectAsync(int port, CancellationToken cancellationToken = default);
}

public enum OpenRgbHostState
{
    /// <summary>No bundled OpenRGB was found next to the app.</summary>
    NotBundled,

    /// <summary>Bundled copy present; this app has not started it.</summary>
    Stopped,

    Starting,

    /// <summary>Started by this app and answering on its port.</summary>
    Running,

    /// <summary>Started by this app, then exited or stopped answering.</summary>
    Failed,
}

/// <summary>
/// The OpenRGB copy bundled with the app, run as a headless SDK server (no
/// window, its own configuration folder, localhost only) for as long as the
/// app runs. A server that is already answering, whoever started it, is used
/// as is; starting a second one would fight it for the devices.
/// </summary>
public interface IOpenRgbHost
{
    /// <summary>Full path of the bundled executable, or null when the app ships without one.</summary>
    string? BundledExecutablePath { get; }

    OpenRgbHostState State { get; }

    /// <summary>Why the last start failed, for the page.</summary>
    string? LastError { get; }

    int Port { get; }

    /// <summary>Starts the bundled server unless one already answers. Returns when the SDK port answers or the start fails.</summary>
    Task<OperationResult<bool>> EnsureRunningAsync(CancellationToken cancellationToken = default);

    /// <summary>Stops the server this app started; a server it did not start is left alone.</summary>
    Task StopAsync();
}
