using ThisIsMyPC.App.Services;
using ThisIsMyPC.App.ViewModels;
using ThisIsMyPC.Core.Results;
using ThisIsMyPC.Core.Services;
using ThisIsMyPC.Core.Settings;

namespace ThisIsMyPC.Integration.Tests.ViewModels;

public sealed class MainWindowViewModelUpdateBadgeTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), $"tipc-badge-{Guid.NewGuid():N}");

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_dir))
                Directory.Delete(_dir, recursive: true);
        }
        catch (IOException) { }
    }

    private MainWindowViewModel CreateViewModel(
        Fakes.FakeUpdateService updateService, SettingsService settings)
    {
        var pendingChangesService = new PendingChangesService();
        var reviewPanel = new ReviewPanelViewModel(pendingChangesService, new Core.Sets.CustomSetWriter(Path.Combine(_dir, "sets")));
        return new MainWindowViewModel(
            new NavigationService([]),
            pendingChangesService,
            new Fakes.FakeChangeHistoryService(),
            new Fakes.FakeRegistryService(),
            new Fakes.FakeExplorerRestartService(),
            reviewPanel,
            new Fakes.FakeSetProvider(),
            [],
            new Core.Sets.CustomSetWriter(Path.Combine(_dir, "sets")),
            new Fakes.FakeRestorePointService(),
            settingsService: settings,
            updateService: updateService);
    }

    private SettingsService CreateSettings()
    {
        var settings = new SettingsService(Path.Combine(_dir, "settings.json"));
        settings.Initialize();
        return settings;
    }

    private static Fakes.FakeUpdateService AvailableUpdate() => new()
    {
        NextResult = OperationResult<UpdateCheckResult>.Success(new UpdateCheckResult(true, "2.0.0", null)),
    };

    [Fact]
    public async Task ManualDownloadRequiresASeparateRestartClick()
    {
        var update = AvailableUpdate();
        var vm = CreateViewModel(update, CreateSettings());
        await vm.InitializeAsync();
        Assert.Equal("Download update", vm.UpdateActionText);
        Assert.Equal(0, update.DownloadCallCount);
        await vm.DownloadOrRestartUpdateCommand.ExecuteAsync(null);
        Assert.Equal(1, update.DownloadCallCount);
        Assert.Equal(0, update.RestartCallCount);
        Assert.Equal("Restart ThisIsMyPC", vm.UpdateActionText);
        await vm.DownloadOrRestartUpdateCommand.ExecuteAsync(null);
        Assert.Equal(1, update.RestartCallCount);
    }

    [Fact]
    public async Task AutomaticDownloadNeverInstalls()
    {
        var settings = CreateSettings();
        settings.SetApp(AppSettingKeys.AutoDownloadUpdates, "1");
        var update = AvailableUpdate();
        var vm = CreateViewModel(update, settings);
        await vm.InitializeAsync();
        Assert.Equal(1, update.DownloadCallCount);
        Assert.True(vm.IsUpdateReady);
        Assert.Equal(0, update.RestartCallCount);
    }

    [Fact]
    public async Task UpdateOptOutPreventsAutomaticDownload()
    {
        var settings = CreateSettings();
        settings.SetApp(AppSettingKeys.AutoDownloadUpdates, "1");
        settings.SetApp(AppSettingKeys.UpdateCheck, "0");
        var update = AvailableUpdate();
        var vm = CreateViewModel(update, settings);
        await vm.InitializeAsync();
        Assert.Equal(0, update.CheckCallCount);
        Assert.Equal(0, update.DownloadCallCount);
        Assert.Equal(0, update.RestartCallCount);
    }

    [Fact]
    public async Task RejectedDownloadRemainsRetryableAndCannotRestart()
    {
        var update = AvailableUpdate();
        update.DownloadResult = OperationResult<bool>.Failure("Verification rejected", ErrorCategory.AccessDenied);
        var vm = CreateViewModel(update, CreateSettings());
        await vm.InitializeAsync();
        await vm.DownloadOrRestartUpdateCommand.ExecuteAsync(null);
        Assert.False(vm.IsUpdateReady);
        Assert.Equal("Download update", vm.UpdateActionText);
        Assert.Equal(0, update.RestartCallCount);
        Assert.Contains("Verification rejected", vm.StatusMessage);
        update.DownloadResult = OperationResult<bool>.Success(true);
        await vm.DownloadOrRestartUpdateCommand.ExecuteAsync(null);
        Assert.True(vm.IsUpdateReady);
        Assert.Equal(0, update.RestartCallCount);
    }

    [Fact]
    public async Task RepeatedClicksDoNotStartConcurrentDownloads()
    {
        var completion = new TaskCompletionSource<OperationResult<bool>>(TaskCreationOptions.RunContinuationsAsynchronously);
        var update = AvailableUpdate();
        update.DownloadHandler = _ => completion.Task;
        var vm = CreateViewModel(update, CreateSettings());
        await vm.InitializeAsync();
        var download = vm.DownloadOrRestartUpdateCommand.ExecuteAsync(null);
        Assert.True(vm.IsUpdateDownloading);
        Assert.False(vm.DownloadOrRestartUpdateCommand.CanExecute(null));
        await vm.DownloadOrRestartUpdateCommand.ExecuteAsync(null);
        Assert.Equal(1, update.DownloadCallCount);
        completion.SetResult(OperationResult<bool>.Success(true));
        await download;
        Assert.True(vm.IsUpdateReady);
        Assert.Equal(0, update.RestartCallCount);
    }

    [Fact]
    public async Task UpdateAvailable_ShowsBadgeWithVersion()
    {
        var update = new Fakes.FakeUpdateService
        {
            NextResult = OperationResult<UpdateCheckResult>.Success(
                new UpdateCheckResult(true, "2.0.0", null)),
        };
        var vm = CreateViewModel(update, CreateSettings());

        await vm.InitializeAsync();

        Assert.Equal(1, update.CheckCallCount);
        Assert.True(vm.IsUpdateBadgeVisible);
        Assert.Equal("Update 2.0.0", vm.UpdateBadgeText);
    }

    [Fact]
    public async Task NoUpdate_NoBadge()
    {
        var update = new Fakes.FakeUpdateService();
        var vm = CreateViewModel(update, CreateSettings());

        await vm.InitializeAsync();

        Assert.Equal(1, update.CheckCallCount);
        Assert.False(vm.IsUpdateBadgeVisible);
    }

    [Fact]
    public async Task OptedOut_NeverCallsTheService()
    {
        var settings = CreateSettings();
        settings.SetApp(AppSettingKeys.UpdateCheck, "0");
        var update = new Fakes.FakeUpdateService();
        var vm = CreateViewModel(update, settings);

        await vm.InitializeAsync();

        Assert.Equal(0, update.CheckCallCount);
        Assert.False(vm.IsUpdateBadgeVisible);
    }

    [Fact]
    public async Task CheckFailure_IsSilent_NoBadgeNoStatus()
    {
        var update = new Fakes.FakeUpdateService
        {
            NextResult = OperationResult<UpdateCheckResult>.Failure(
                "offline", ErrorCategory.ServiceUnavailable),
        };
        var vm = CreateViewModel(update, CreateSettings());

        await vm.InitializeAsync();

        Assert.False(vm.IsUpdateBadgeVisible);
        Assert.Equal(string.Empty, vm.StatusMessage);
    }
}
