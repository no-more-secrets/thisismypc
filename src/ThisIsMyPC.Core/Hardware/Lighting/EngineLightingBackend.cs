using ThisIsMyPC.Core.Results;

namespace ThisIsMyPC.Core.Hardware.Lighting;

/// <summary>
/// The lighting backend over the bundled engine. Detection starts the engine
/// and reads its device list through the SDK protocol; sessions are SDK
/// connections to the same engine, so the page's writes reach every device the
/// engine drives.
/// </summary>
public sealed class EngineLightingBackend : ILightingBackend, IDisposable
{
    private readonly ILightingEngine _engine;
    private readonly IOpenRgbClient _client;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private LightingInventory? _inventory;
    private bool _started;

    public EngineLightingBackend(ILightingEngine engine, IOpenRgbClient client)
    {
        ArgumentNullException.ThrowIfNull(engine);
        ArgumentNullException.ThrowIfNull(client);
        _engine = engine;
        _client = client;
    }

    /// <summary>The last inventory, or null before the first detection.</summary>
    public LightingInventory? Current => _inventory;

    public async Task<OperationResult<LightingInventory>> DetectAsync(bool rescan = false, CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (_inventory is not null && !rescan)
                return OperationResult<LightingInventory>.Success(_inventory);

            var rescanRunningEngine = rescan && _started;
            var started = await _engine.StartAsync(cancellationToken).ConfigureAwait(false);
            if (!started.IsSuccess)
                return OperationResult<LightingInventory>.Failure(started.ErrorMessage ?? "The lighting engine did not start.", started.ErrorCategory ?? ErrorCategory.ServiceUnavailable, started.Exception);
            _started = true;

            if (rescanRunningEngine)
            {
                var rescanned = await _engine.RescanAsync(cancellationToken).ConfigureAwait(false);
                if (!rescanned.IsSuccess)
                    return OperationResult<LightingInventory>.Failure(rescanned.ErrorMessage ?? "The lighting engine did not rescan.", rescanned.ErrorCategory ?? ErrorCategory.ServiceUnavailable, rescanned.Exception);
            }

            var connected = await _client.ConnectAsync(started.Value!, cancellationToken).ConfigureAwait(false);
            if (!connected.IsSuccess)
                return OperationResult<LightingInventory>.Failure(connected.ErrorMessage ?? "The lighting engine did not answer.", connected.ErrorCategory ?? ErrorCategory.ServiceUnavailable, connected.Exception);

            using var session = connected.Value!;
            var devices = await session.GetDevicesAsync(cancellationToken).ConfigureAwait(false);
            if (!devices.IsSuccess)
                return OperationResult<LightingInventory>.Failure(devices.ErrorMessage ?? "The lighting engine did not list its devices.", devices.ErrorCategory ?? ErrorCategory.ServiceUnavailable, devices.Exception);

            var summaries = devices.Value!
                .Select(device => new LightingDeviceSummary(device.Name, device.Type, ControllerOf(device), device.Location))
                .ToList();
            _inventory = new LightingInventory(summaries, _engine.Notes.ToList(), DateTimeOffset.UtcNow);
            return OperationResult<LightingInventory>.Success(_inventory);
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<OperationResult<ILightingSession>> OpenAsync(CancellationToken cancellationToken = default)
    {
        var started = await _engine.StartAsync(cancellationToken).ConfigureAwait(false);
        if (!started.IsSuccess)
            return OperationResult<ILightingSession>.Failure(started.ErrorMessage ?? "The lighting engine did not start.", started.ErrorCategory ?? ErrorCategory.ServiceUnavailable, started.Exception);
        _started = true;
        return await _client.ConnectAsync(started.Value!, cancellationToken).ConfigureAwait(false);
    }

    public void Dispose() => _gate.Dispose();

    /// <summary>The engine reports a device's driver family in its description; a blank one still names the engine.</summary>
    private static string ControllerOf(LightingDevice device)
        => string.IsNullOrWhiteSpace(device.Description) ? "Lighting engine" : device.Description;
}
