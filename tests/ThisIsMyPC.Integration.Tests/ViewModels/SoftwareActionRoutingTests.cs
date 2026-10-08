using ThisIsMyPC.App.Services;
using ThisIsMyPC.App.ViewModels;
using ThisIsMyPC.Core.Actions;
using ThisIsMyPC.Core.Changes;
using ThisIsMyPC.Core.Packages;
using ThisIsMyPC.Core.Results;
using ThisIsMyPC.Core.Services;
using ThisIsMyPC.Ipc.Contracts;
using ThisIsMyPC.Modules.Software;
using ThisIsMyPC.Modules.Software.Actions;
using ThisIsMyPC.Modules.Software.Services;

namespace ThisIsMyPC.Integration.Tests.ViewModels;

public sealed class SoftwareActionRoutingTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "tipc-software-routing-" + Guid.NewGuid().ToString("N"));

    [Theory]
    [InlineData("install", false)]
    [InlineData("uninstall", false)]
    [InlineData("upgrade", false)]
    [InlineData("uninstall", true)]
    public async Task WingetActionsRunLocally_EvenBesideElevatedActions(string operation, bool mixed)
    {
        var changes = new PendingChangesService();
        var actions = new PendingActionsService();
        var broker = new Broker();
        var winget = new Winget();
        var module = new SoftwareModule(winget, new Appx());
        var writer = new Core.Sets.CustomSetWriter(Path.Combine(_root, "sets"));
        var vm = new MainWindowViewModel(new NavigationService([module]), changes,
            new Fakes.FakeChangeHistoryService(), new Fakes.FakeRegistryService(), new Fakes.FakeExplorerRestartService(),
            new ReviewPanelViewModel(changes, writer), new Fakes.FakeSetProvider(), [], writer,
            new Fakes.FakeRestorePointService(), pendingActionsService: actions, privilegeBroker: broker);
        await vm.InitializeAsync();
        var cursor = SoftwareCatalog.Entries.Single(e => e.Id == "cursor");
        actions.Stage(operation switch
        {
            "install" => SoftwareActionFactory.CreateInstall(cursor),
            "uninstall" => SoftwareActionFactory.CreateUninstall(cursor),
            _ => SoftwareActionFactory.CreateUpgrade(new(cursor.WingetId, cursor.Name, "1.0", "2.0")),
        });
        if (mixed) actions.Stage(SoftwareActionFactory.CreateAppxRemove(WindowsAppsCatalog.Entries[0]));

        await vm.ApplyAllCommand.ExecuteAsync(null);

        Assert.Equal(0, actions.PendingCount);
        Assert.Equal(operation, Assert.Single(winget.Operations));
        var request = Assert.Single(broker.Requests);
        Assert.Single(request.ReviewOnly);
        if (mixed)
        {
            Assert.StartsWith("appx-remove:", Assert.Single(request.Actions).ActionId);
            Assert.StartsWith("appx-remove:", Assert.Single(broker.Session.Actions).ActionId);
        }
        else Assert.Empty(request.Actions);
    }

    public void Dispose()
    {
        if (Directory.Exists(_root)) Directory.Delete(_root, true);
    }

    private sealed class Winget : IWingetService
    {
        public List<string> Operations { get; } = [];
        public Task<OperationResult<string>> GetVersionAsync(CancellationToken cancellationToken = default)
            => Task.FromResult(OperationResult<string>.Success("test"));
        public Task<OperationResult<IReadOnlyList<InstalledWingetPackage>>> ListInstalledAsync(CancellationToken cancellationToken = default)
            => Task.FromResult(OperationResult<IReadOnlyList<InstalledWingetPackage>>.Success([new("Anysphere.Cursor", "1.0", "Cursor (User)")]));
        public Task<OperationResult<IReadOnlyList<UpgradableWingetPackage>>> ListUpgradableAsync(CancellationToken cancellationToken = default)
            => Task.FromResult(OperationResult<IReadOnlyList<UpgradableWingetPackage>>.Success([]));
        public Task<OperationResult<bool>> InstallAsync(string packageId, WingetSource source, CancellationToken cancellationToken = default) => Record("install");
        public Task<OperationResult<bool>> UninstallAsync(string packageId, WingetSource source, CancellationToken cancellationToken = default) => Record("uninstall");
        public Task<OperationResult<bool>> UpgradeAsync(string packageId, CancellationToken cancellationToken = default) => Record("upgrade");
        private Task<OperationResult<bool>> Record(string operation)
        {
            Operations.Add(operation);
            return Task.FromResult(OperationResult<bool>.Success(true));
        }
    }

    private sealed class Appx : IAppxPackageService
    {
        public Task<OperationResult<IReadOnlyList<AppxPackageInfo>>> EnumeratePackagesAsync(CancellationToken cancellationToken = default)
            => Task.FromResult(OperationResult<IReadOnlyList<AppxPackageInfo>>.Success([]));
        public Task<OperationResult<AppxPackageInfo>> QueryPackageAsync(string packageFullName, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<OperationResult<bool>> RemovePackageAsync(string packageFullName, bool allUsers = false, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<OperationResult<bool>> DeprovisionPackageAsync(string packageFamilyName, CancellationToken cancellationToken = default) => throw new NotSupportedException();
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
        public List<ActionDescriptor> Actions { get; } = [];
        public Task<OperationResult<bool>> ExecuteActionAsync(ActionDescriptor action, CancellationToken cancellationToken = default)
        {
            Actions.Add(action);
            return Task.FromResult(OperationResult<bool>.Success(true));
        }
        public Task<OperationResult<bool>> ApplyChangeAsync(ChangeDescriptor change, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<OperationResult<bool>> RevertChangeAsync(ChangeDescriptor change, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<RestorePointResult> CreateRestorePointAsync(string description, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<OperationResult<bool>> EnableOwnerModeAsync(CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<OperationResult<bool>> DisableOwnerModeAsync(CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}
