using ThisIsMyPC.Core.Hardware.Lighting;
using ThisIsMyPC.Core.Results;
using ThisIsMyPC.Lighting.Controllers;
using ThisIsMyPC.Lighting.Detection;
using ThisIsMyPC.Lighting.Transport;

namespace ThisIsMyPC.Lighting;

/// <summary>
/// The built-in lighting backend: runs every registered detector over the
/// HID collections and the I2C buses this process can reach, keeps the
/// controllers it found, and hands out sessions over them. One detection
/// pass at a time; every controller call runs off the caller's thread under
/// that controller's own lock, so a slow SMBus write never blocks the page
/// or another device.
/// </summary>
public sealed class NativeLightingBackend : ILightingBackend, IDisposable
{
    private readonly IHidTransport? _hid;
    private readonly II2cBusProvider? _i2c;
    private readonly IReadOnlyList<HidDetector> _hidDetectors;
    private readonly IReadOnlyList<I2cPciDetector> _i2cDetectors;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly List<Session> _sessions = [];
    private List<Entry> _entries = [];
    private LightingInventory? _inventory;
    private bool _disposed;

    /// <param name="hid">HID transport, or null when this process has none (detection then notes it).</param>
    /// <param name="i2c">I2C bus provider, or null when this process has none.</param>
    public NativeLightingBackend(
        IHidTransport? hid,
        II2cBusProvider? i2c,
        IReadOnlyList<HidDetector>? hidDetectors = null,
        IReadOnlyList<I2cPciDetector>? i2cDetectors = null)
    {
        _hid = hid;
        _i2c = i2c;
        _hidDetectors = hidDetectors ?? LightingDetectors.Hid;
        _i2cDetectors = i2cDetectors ?? LightingDetectors.I2cPci;
    }

    internal sealed class Entry(ILightingController controller, LightingDeviceSummary summary)
    {
        public ILightingController Controller { get; } = controller;
        public LightingDeviceSummary Summary { get; } = summary;
        public object Lock { get; } = new();
    }

    /// <summary>The last pass, or null before the first.</summary>
    public LightingInventory? Current => _inventory;

    public async Task<OperationResult<LightingInventory>> DetectAsync(bool rescan = false, CancellationToken cancellationToken = default)
    {
        if (_disposed)
            return OperationResult<LightingInventory>.Failure("The lighting backend is closed.", ErrorCategory.ServiceUnavailable);
        if (!rescan && _inventory is { } cached)
            return OperationResult<LightingInventory>.Success(cached);

        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (!rescan && _inventory is { } raced)
                return OperationResult<LightingInventory>.Success(raced);
            var (entries, inventory) = await Task.Run(Detect, cancellationToken).ConfigureAwait(false);
            var old = _entries;
            _entries = entries;
            _inventory = inventory;
            // A call in flight on an old controller finishes under its lock
            // before the controller closes its handles.
            foreach (var entry in old)
            {
                lock (entry.Lock)
                    entry.Controller.Dispose();
            }
            if (old.Count > 0 || entries.Count > 0)
            {
                Session[] sessions;
                lock (_sessions)
                    sessions = _sessions.ToArray();
                foreach (var session in sessions)
                    session.RaiseDeviceListChanged();
            }
            return OperationResult<LightingInventory>.Success(inventory);
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <summary>One synchronous pass. Internal for tests, which call it on a scripted transport.</summary>
    internal (List<Entry> Entries, LightingInventory Inventory) Detect()
    {
        var notes = new List<string>();
        var entries = new List<Entry>();

        if (_hid is null)
        {
            notes.Add("HID devices: no transport in this process.");
        }
        else
        {
            var collections = SafeEnumerate(notes);
            for (var i = 0; i < collections.Count; i++)
            {
                var info = collections[i];
                foreach (var detector in _hidDetectors)
                {
                    if (!detector.Matches(info))
                        continue;
                    var remaining = collections.Skip(i).ToList();
                    ILightingController? controller;
                    try
                    {
                        controller = detector.Detect(_hid, info, remaining, detector.Name, notes);
                    }
#pragma warning disable CA1031 // One misbehaving device must not hide the others.
                    catch (Exception ex)
                    {
                        notes.Add($"{detector.Name}: detection failed ({ex.GetType().Name}: {ex.Message}).");
                        continue;
                    }
#pragma warning restore CA1031
                    if (controller is null)
                        continue;
                    var device = controller.Describe(entries.Count);
                    entries.Add(new Entry(controller, new LightingDeviceSummary(device.Name, device.Type, controller.Family, device.Location)));
                    notes.Add($"{detector.Name}: found at {device.Location}.");
                }
            }
        }

        if (_i2c is null)
        {
            notes.Add("I2C buses: no transport in this process.");
        }
        else
        {
            IReadOnlyList<II2cBus> buses;
            try
            {
                buses = _i2c.Enumerate(notes);
            }
#pragma warning disable CA1031
            catch (Exception ex)
            {
                notes.Add($"I2C buses: enumeration failed ({ex.GetType().Name}: {ex.Message}).");
                buses = [];
            }
#pragma warning restore CA1031
            foreach (var bus in buses)
            {
                foreach (var detector in _i2cDetectors)
                {
                    if (!detector.Matches(bus.Info))
                        continue;
                    ILightingController? controller;
                    try
                    {
                        controller = detector.Detect(bus, detector.Address, detector.Name, notes);
                    }
#pragma warning disable CA1031
                    catch (Exception ex)
                    {
                        notes.Add($"{detector.Name}: detection failed ({ex.GetType().Name}: {ex.Message}).");
                        continue;
                    }
#pragma warning restore CA1031
                    if (controller is null)
                        continue;
                    var device = controller.Describe(entries.Count);
                    entries.Add(new Entry(controller, new LightingDeviceSummary(device.Name, device.Type, controller.Family, device.Location)));
                    notes.Add($"{detector.Name}: found at {device.Location}.");
                    break;
                }
            }
        }

        var inventory = new LightingInventory(entries.Select(e => e.Summary).ToList(), notes, DateTimeOffset.Now);
        return (entries, inventory);
    }

    private IReadOnlyList<HidDeviceInfo> SafeEnumerate(List<string> notes)
    {
        try
        {
            var list = _hid!.Enumerate();
            notes.Add($"HID collections enumerated: {list.Count}.");
            return list;
        }
#pragma warning disable CA1031
        catch (Exception ex)
        {
            notes.Add($"HID devices: enumeration failed ({ex.GetType().Name}: {ex.Message}).");
            return [];
        }
#pragma warning restore CA1031
    }

    public async Task<OperationResult<ILightingSession>> OpenAsync(CancellationToken cancellationToken = default)
    {
        var detected = await DetectAsync(rescan: false, cancellationToken).ConfigureAwait(false);
        if (!detected.IsSuccess)
            return OperationResult<ILightingSession>.Failure(detected.ErrorMessage ?? "Detection failed.", detected.ErrorCategory ?? ErrorCategory.ServiceUnavailable);
        var session = new Session(this);
        lock (_sessions)
            _sessions.Add(session);
        return OperationResult<ILightingSession>.Success(session);
    }

    private void Close(Session session)
    {
        lock (_sessions)
            _sessions.Remove(session);
    }

    private Task<OperationResult<T>> RunAsync<T>(int index, Func<Entry, OperationResult<T>> call, CancellationToken cancellationToken)
    {
        var entries = _entries;
        if (index < 0 || index >= entries.Count)
            return Task.FromResult(OperationResult<T>.Failure("That lighting device is no longer present.", ErrorCategory.NotFound));
        var entry = entries[index];
        return Task.Run(() =>
        {
            lock (entry.Lock)
            {
                try
                {
                    return call(entry);
                }
#pragma warning disable CA1031 // A transport fault is a failed write, not a crash.
                catch (Exception ex)
                {
                    return OperationResult<T>.Failure($"{entry.Summary.Name}: {ex.Message}", ErrorCategory.ServiceUnavailable, ex);
                }
#pragma warning restore CA1031
            }
        }, cancellationToken);
    }

    private sealed class Session(NativeLightingBackend owner) : ILightingSession
    {
        private bool _disposed;

        public bool IsConnected => !_disposed && !owner._disposed;

        public event EventHandler? DeviceListChanged;

        public void RaiseDeviceListChanged() => DeviceListChanged?.Invoke(this, EventArgs.Empty);

        public Task<OperationResult<IReadOnlyList<LightingDevice>>> GetDevicesAsync(CancellationToken cancellationToken = default)
        {
            var entries = owner._entries;
            return Task.Run(() =>
            {
                var devices = new List<LightingDevice>(entries.Count);
                for (var i = 0; i < entries.Count; i++)
                {
                    lock (entries[i].Lock)
                        devices.Add(entries[i].Controller.Describe(i));
                }
                return OperationResult<IReadOnlyList<LightingDevice>>.Success(devices);
            }, cancellationToken);
        }

        public Task<OperationResult<LightingDevice>> GetDeviceAsync(int deviceIndex, CancellationToken cancellationToken = default) =>
            owner.RunAsync(deviceIndex, entry => OperationResult<LightingDevice>.Success(entry.Controller.Describe(deviceIndex)), cancellationToken);

        public Task<OperationResult<bool>> SetModeAsync(int deviceIndex, LightingMode mode, CancellationToken cancellationToken = default) =>
            owner.RunAsync(deviceIndex, entry => entry.Controller.SetMode(mode), cancellationToken);

        public Task<OperationResult<bool>> SetLedsAsync(int deviceIndex, IReadOnlyList<RgbColor> colors, CancellationToken cancellationToken = default) =>
            owner.RunAsync(deviceIndex, entry => entry.Controller.SetLeds(colors), cancellationToken);

        public Task<OperationResult<bool>> SetZoneLedsAsync(int deviceIndex, int zoneIndex, IReadOnlyList<RgbColor> colors, CancellationToken cancellationToken = default) =>
            owner.RunAsync(deviceIndex, entry => entry.Controller.SetZoneLeds(zoneIndex, colors), cancellationToken);

        public Task<OperationResult<bool>> SaveModeAsync(int deviceIndex, LightingMode mode, CancellationToken cancellationToken = default) =>
            owner.RunAsync(deviceIndex, entry => entry.Controller.SaveMode(mode), cancellationToken);

        public void Dispose()
        {
            if (_disposed)
                return;
            _disposed = true;
            owner.Close(this);
        }
    }

    /// <summary>Closes every controller. The gate is left for the collector: a pass still unwinding must be able to release it.</summary>
    public void Dispose()
    {
        if (_disposed)
            return;
        _disposed = true;
        var entries = _entries;
        _entries = [];
        foreach (var entry in entries)
        {
            lock (entry.Lock)
                entry.Controller.Dispose();
        }
    }
}
