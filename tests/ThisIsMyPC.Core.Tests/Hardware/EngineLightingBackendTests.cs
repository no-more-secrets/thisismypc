using ThisIsMyPC.Core.Hardware.Lighting;
using ThisIsMyPC.Core.Results;

namespace ThisIsMyPC.Core.Tests.Hardware;

/// <summary>The engine backend starts the engine once, lists devices over the SDK, and rescans a running engine.</summary>
public class EngineLightingBackendTests
{
    private sealed class FakeEngine : ILightingEngine
    {
        public bool IsAvailable { get; set; } = true;
        public int Starts { get; private set; }
        public int Rescans { get; private set; }
        public OperationResult<LightingEngineEndpoint> StartResult { get; set; } = OperationResult<LightingEngineEndpoint>.Success(new(6790, new string('A', 64)));
        public List<string> NoteList { get; } = ["engine line"];
        public IReadOnlyList<string> Notes => NoteList;

        public Task<OperationResult<LightingEngineEndpoint>> StartAsync(CancellationToken cancellationToken = default)
        {
            Starts++;
            return Task.FromResult(StartResult);
        }

        public Task<OperationResult<bool>> RescanAsync(CancellationToken cancellationToken = default)
        {
            Rescans++;
            return Task.FromResult(OperationResult<bool>.Success(true));
        }
    }

    private sealed class FakeSession : ILightingSession
    {
        public List<LightingDevice> Devices { get; } = [];
        public bool Disposed { get; private set; }
        public bool IsConnected => !Disposed;
        public event EventHandler? DeviceListChanged { add { } remove { } }
        public Task<OperationResult<IReadOnlyList<LightingDevice>>> GetDevicesAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(OperationResult<IReadOnlyList<LightingDevice>>.Success(Devices.ToList()));
        public Task<OperationResult<LightingDevice>> GetDeviceAsync(int deviceIndex, CancellationToken cancellationToken = default) =>
            Task.FromResult(OperationResult<LightingDevice>.Success(Devices[deviceIndex]));
        public Task<OperationResult<bool>> SetModeAsync(int deviceIndex, LightingMode mode, CancellationToken cancellationToken = default) =>
            Task.FromResult(OperationResult<bool>.Success(true));
        public Task<OperationResult<bool>> SetLedsAsync(int deviceIndex, IReadOnlyList<RgbColor> colors, CancellationToken cancellationToken = default) =>
            Task.FromResult(OperationResult<bool>.Success(true));
        public Task<OperationResult<bool>> SetZoneLedsAsync(int deviceIndex, int zoneIndex, IReadOnlyList<RgbColor> colors, CancellationToken cancellationToken = default) =>
            Task.FromResult(OperationResult<bool>.Success(true));
        public Task<OperationResult<bool>> SaveModeAsync(int deviceIndex, LightingMode mode, CancellationToken cancellationToken = default) =>
            Task.FromResult(OperationResult<bool>.Success(true));
        public void Dispose() => Disposed = true;
    }

    private sealed class FakeClient : IOpenRgbClient
    {
        public List<int> Ports { get; } = [];
        public List<string> Tokens { get; } = [];
        public Func<FakeSession> SessionFactory { get; set; } = () => new FakeSession();
        public FakeSession? Last { get; private set; }

        public Task<OperationResult<ILightingSession>> ConnectAsync(LightingEngineEndpoint endpoint, CancellationToken cancellationToken = default)
        {
            Ports.Add(endpoint.Port);
            Tokens.Add(endpoint.AuthenticationToken);
            Last = SessionFactory();
            return Task.FromResult(OperationResult<ILightingSession>.Success(Last));
        }
    }

    private static LightingDevice Device(string name, string description = "ASUS Aura USB") => new()
    {
        Index = 0,
        Name = name,
        Type = LightingDeviceType.Motherboard,
        Description = description,
        Location = "HID: \\\\?\\hid#vid_0b05&pid_1939",
    };

    [Fact]
    public async Task Detect_StartsTheEngineOnceAndListsDevicesFromTheSdk()
    {
        var engine = new FakeEngine();
        var client = new FakeClient
        {
            SessionFactory = () =>
            {
                var session = new FakeSession();
                session.Devices.Add(Device("ROG STRIX B550-F GAMING"));
                return session;
            },
        };
        using var backend = new EngineLightingBackend(engine, client);

        var first = await backend.DetectAsync();
        var second = await backend.DetectAsync();

        Assert.True(first.IsSuccess, first.ErrorMessage);
        var device = Assert.Single(first.Value!.Devices);
        Assert.Equal("ROG STRIX B550-F GAMING", device.Name);
        Assert.Equal("ASUS Aura USB", device.Controller);
        Assert.Contains("engine line", first.Value.Notes);
        Assert.Same(first.Value, second.Value);
        Assert.Equal(1, engine.Starts);
        Assert.Equal([6790], client.Ports);
        Assert.Equal(new string('A', 64), Assert.Single(client.Tokens));
        Assert.True(client.Last!.Disposed);
    }

    [Fact]
    public async Task Detect_WithRescan_AsksARunningEngineToDetectAgain()
    {
        var engine = new FakeEngine();
        var client = new FakeClient();
        using var backend = new EngineLightingBackend(engine, client);

        await backend.DetectAsync();
        Assert.Equal(0, engine.Rescans);
        await backend.DetectAsync(rescan: true);

        Assert.Equal(1, engine.Rescans);
        Assert.Equal(2, client.Ports.Count);
    }

    [Fact]
    public async Task Detect_ReportsWhyTheEngineDidNotStart()
    {
        var engine = new FakeEngine { StartResult = OperationResult<LightingEngineEndpoint>.Failure("The lighting engine did not start: it exited with code 1.", ErrorCategory.ServiceUnavailable) };
        using var backend = new EngineLightingBackend(engine, new FakeClient());

        var result = await backend.DetectAsync();

        Assert.False(result.IsSuccess);
        Assert.Contains("did not start", result.ErrorMessage, StringComparison.Ordinal);
        Assert.Null(backend.Current);
    }

    [Fact]
    public async Task Open_ReturnsAnSdkSessionOnTheEnginePort()
    {
        var engine = new FakeEngine();
        var client = new FakeClient();
        using var backend = new EngineLightingBackend(engine, client);

        var session = await backend.OpenAsync();

        Assert.True(session.IsSuccess);
        Assert.Same(client.Last, session.Value);
        Assert.Equal([6790], client.Ports);
        Assert.Equal(new string('A', 64), Assert.Single(client.Tokens));
    }

    [Fact]
    public void Device_WithoutDescription_IsCreditedToTheEngine()
    {
        var engine = new FakeEngine();
        var client = new FakeClient
        {
            SessionFactory = () =>
            {
                var session = new FakeSession();
                session.Devices.Add(Device("Nameless", description: ""));
                return session;
            },
        };
        using var backend = new EngineLightingBackend(engine, client);

        var result = backend.DetectAsync().GetAwaiter().GetResult();

        Assert.Equal("Lighting engine", Assert.Single(result.Value!.Devices).Controller);
    }
}
