using ThisIsMyPC.App.Services;
using ThisIsMyPC.App.ViewModels;
using ThisIsMyPC.Core.Actions;
using ThisIsMyPC.Core.Changes;
using ThisIsMyPC.Core.Hardware;
using ThisIsMyPC.Core.Modules;
using ThisIsMyPC.Core.Results;
using ThisIsMyPC.Core.Services;
using ThisIsMyPC.Ipc.Contracts;
using ThisIsMyPC.Modules.Hardware;
using ThisIsMyPC.Modules.Hardware.Cooling;

namespace ThisIsMyPC.Integration.Tests.ViewModels;

public sealed class CoolingProfileRoutingTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "tipc-cooling-routing-" + Guid.NewGuid().ToString("N"));
    private const string Profile = """{"__VERSION__":"226","Main":{"Controls":[],"FanCurves":[]}}""";

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Apply_SavesProfileLocally_AndSendsOnlySystemChangesToBroker(bool mixed)
    {
        Directory.CreateDirectory(Path.Combine(_root, "Configurations"));
        await File.WriteAllTextAsync(Path.Combine(_root, "Configurations", "source.json"), Profile);
        var facts = new Facts(Path.Combine(_root, "FanControl.exe"));
        var store = new FanControlProfileStore(facts);
        var source = await store.ReadAsync("source.json");
        Assert.True(source.IsSuccess, source.ErrorMessage);
        var change = await store.BuildChangeAsync(source.Value!, "copy.json");
        Assert.True(change.IsSuccess, change.ErrorMessage);
        var pending = new PendingChangesService();
        var broker = new Broker();
        var vm = Create(pending, broker, new CoolingModule(facts), new Fakes.FakeModule("SystemTest"));
        await vm.InitializeAsync();
        pending.Stage(change.Value!);
        if (mixed) pending.Stage(SystemChange());

        await vm.ApplyAllCommand.ExecuteAsync(null);

        Assert.Equal(0, pending.PendingCount);
        Assert.Equal(Profile, await File.ReadAllTextAsync(Path.Combine(_root, "Configurations", "copy.json")));
        Assert.Equal(mixed ? 1 : 0, broker.Requests.Count);
        if (mixed)
        {
            Assert.Equal("SystemTest", Assert.Single(broker.Requests[0].Changes).ModuleId);
            Assert.Equal("SystemTest", Assert.Single(broker.Session.Applied).ModuleId);
        }
    }

    [Fact]
    public async Task ManyLocalProfiles_DoNotRequestElevationOrSystemRestore()
    {
        Directory.CreateDirectory(Path.Combine(_root, "Configurations"));
        await File.WriteAllTextAsync(Path.Combine(_root, "Configurations", "source.json"), Profile);
        var facts = new Facts(Path.Combine(_root, "FanControl.exe"));
        var store = new FanControlProfileStore(facts);
        var source = await store.ReadAsync("source.json");
        var pending = new PendingChangesService();
        var broker = new Broker();
        var vm = Create(pending, broker, new CoolingModule(facts));
        await vm.InitializeAsync();
        for (var i = 0; i < 6; i++)
        {
            var change = await store.BuildChangeAsync(source.Value!, $"copy-{i}.json");
            pending.Stage(change.Value!);
        }

        await vm.ApplyAllCommand.ExecuteAsync(null);

        Assert.Equal(0, pending.PendingCount);
        Assert.Empty(broker.Requests);
        Assert.Equal(7, Directory.GetFiles(Path.Combine(_root, "Configurations")).Length);
    }

    private MainWindowViewModel Create(PendingChangesService pending, Broker broker, params IModule[] modules)
    {
        var writer = new Core.Sets.CustomSetWriter(Path.Combine(_root, "sets"));
        return new MainWindowViewModel(new NavigationService(modules), pending,
            new Fakes.FakeChangeHistoryService(), new Fakes.FakeRegistryService(), new Fakes.FakeExplorerRestartService(),
            new ReviewPanelViewModel(pending, writer), new Fakes.FakeSetProvider(), [], writer,
            new Fakes.FakeRestorePointService(), privilegeBroker: broker);
    }

    private static ChangeDescriptor SystemChange() => new()
    {
        ModuleId = "SystemTest", SettingId = "setting", DisplayName = "System setting", SystemLocation = "HKLM\\Test",
        BeforeValue = "0", AfterValue = "1", BeforeDisplay = "Off", AfterDisplay = "On", ValueType = ChangeValueType.Registry_DWord,
    };

    public void Dispose()
    {
        if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true);
    }

    private sealed class Facts(string executable) : IHardwareFactsProvider
    {
        public HardwareDetectionSnapshot? Current { get; } = new(ObservedHardwareFacts.Empty, new HardwareSnapshot(),
            new Dictionary<CompanionApp, string> { [CompanionApp.FanControl] = executable }, [], DateTimeOffset.Now);
        public event EventHandler? Changed { add { } remove { } }
        public Task<HardwareDetectionSnapshot> GetAsync(bool refresh = false, CancellationToken cancellationToken = default) => Task.FromResult(Current!);
    }

    private sealed class Broker : IPrivilegeBrokerClient
    {
        public List<BrokerSessionRequest> Requests { get; } = [];
        public Session Session { get; } = new();
        public Task<OperationResult<IPrivilegeBrokerSession>> OpenSessionAsync(BrokerSessionRequest request, CancellationToken cancellationToken = default)
        {
            Requests.Add(request);
            return Task.FromResult(OperationResult<IPrivilegeBrokerSession>.Success(Session));
        }
    }

    private sealed class Session : IPrivilegeBrokerSession
    {
        public List<ChangeDescriptor> Applied { get; } = [];
        public Task<OperationResult<bool>> ApplyChangeAsync(ChangeDescriptor change, CancellationToken cancellationToken = default)
        {
            Applied.Add(change);
            return Task.FromResult(OperationResult<bool>.Success(true));
        }
        public Task<OperationResult<bool>> RevertChangeAsync(ChangeDescriptor change, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<OperationResult<bool>> ExecuteActionAsync(ActionDescriptor action, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<RestorePointResult> CreateRestorePointAsync(string description, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<OperationResult<bool>> EnableOwnerModeAsync(CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<OperationResult<bool>> DisableOwnerModeAsync(CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}
