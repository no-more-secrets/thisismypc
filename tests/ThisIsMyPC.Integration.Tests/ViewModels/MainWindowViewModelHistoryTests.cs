using ThisIsMyPC.App.Services;
using ThisIsMyPC.App.ViewModels;
using ThisIsMyPC.Core.Changes;
using ThisIsMyPC.Core.Modules;
using ThisIsMyPC.Core.Results;
using ThisIsMyPC.Core.Services;

namespace ThisIsMyPC.Integration.Tests.ViewModels;

public class MainWindowViewModelHistoryTests
{
    private static MainWindowViewModel CreateViewModel(
        out PendingChangesService pendingChangesService,
        out Fakes.FakeChangeHistoryService historyService,
        params IModule[] modules)
    {
        var navigationService = new NavigationService(modules);
        pendingChangesService = new PendingChangesService();
        historyService = new Fakes.FakeChangeHistoryService();
        var reviewPanel = new ReviewPanelViewModel(pendingChangesService);
        var registryService = new Fakes.FakeRegistryService();
        return new MainWindowViewModel(navigationService, pendingChangesService, historyService, registryService, reviewPanel);
    }

    private static ChangeDescriptor CreateTestChange(string moduleId = "TestModule", string settingId = "s1") => new()
    {
        ModuleId = moduleId,
        SettingId = settingId,
        DisplayName = "Test Setting",
        SystemLocation = @"HKLM\Test",
        BeforeValue = "0",
        AfterValue = "1",
        BeforeDisplay = "Disabled",
        AfterDisplay = "Enabled",
        ValueType = ChangeValueType.Registry_DWord,
        Category = ChangeCategory.Enable,
    };

    [Fact]
    public async Task ApplyAllAsync_RecordsChangesToHistoryOnSuccess()
    {
        var fakeModule = new Fakes.FakeModule("TestModule", _ =>
            Task.FromResult(OperationResult<bool>.Success(true)));

        var vm = CreateViewModel(out var service, out var historyService, fakeModule);
        await vm.InitializeAsync();

        service.Stage(CreateTestChange("TestModule"));
        await vm.ApplyAllCommand.ExecuteAsync(null);

        Assert.Single(historyService.RecordedResults);
        Assert.True(historyService.RecordedResults[0].IsSuccess);
    }

    [Fact]
    public async Task ApplyAllAsync_DoesNotRecordChangesOnFailure()
    {
        var fakeModule = new Fakes.FakeModule("TestModule", _ =>
            Task.FromResult(OperationResult<bool>.Failure("error", ErrorCategory.AccessDenied)));

        var vm = CreateViewModel(out var service, out var historyService, fakeModule);
        await vm.InitializeAsync();

        service.Stage(CreateTestChange("TestModule"));
        await vm.ApplyAllCommand.ExecuteAsync(null);

        Assert.Empty(historyService.RecordedResults);
    }

    [Fact]
    public void OpenHistoryPanelCommand_SetsIsHistoryPanelOpen()
    {
        var vm = CreateViewModel(out _, out _);

        vm.OpenHistoryPanelCommand.Execute(null);

        Assert.True(vm.IsHistoryPanelOpen);
    }

    [Fact]
    public void CloseHistoryPanelCommand_ClearsIsHistoryPanelOpen()
    {
        var vm = CreateViewModel(out _, out _);
        vm.IsHistoryPanelOpen = true;

        vm.CloseHistoryPanelCommand.Execute(null);

        Assert.False(vm.IsHistoryPanelOpen);
    }
}
