using ThisIsMyPC.App.ViewModels;
using ThisIsMyPC.Core.Changes;
using ThisIsMyPC.Core.Services;

namespace ThisIsMyPC.Integration.Tests.ViewModels;

public class ReviewPanelViewModelTests
{
    private static ChangeDescriptor CreateTestChange(
        string moduleId = "test",
        string settingId = "setting1",
        ChangeCategory category = ChangeCategory.Enable) => new()
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
        Category = category,
    };

    [Fact]
    public void ReviewItems_PopulatesFromPendingChangesService()
    {
        var service = new PendingChangesService();
        service.Stage(CreateTestChange());
        var vm = new ReviewPanelViewModel(service);

        Assert.Single(vm.ReviewItems);
        Assert.Equal("Test Setting", vm.ReviewItems[0].DisplayName);
        Assert.Equal(@"HKLM\Test", vm.ReviewItems[0].SystemLocation);
        Assert.Equal("Disabled", vm.ReviewItems[0].BeforeDisplay);
        Assert.Equal("Enabled", vm.ReviewItems[0].AfterDisplay);
    }

    [Fact]
    public void ReviewItems_UpdatesWhenChangeStaged()
    {
        var service = new PendingChangesService();
        var vm = new ReviewPanelViewModel(service);

        Assert.Empty(vm.ReviewItems);

        service.Stage(CreateTestChange());

        Assert.Single(vm.ReviewItems);
    }

    [Fact]
    public void ReviewItems_ClearsWhenDiscardAll()
    {
        var service = new PendingChangesService();
        service.Stage(CreateTestChange());
        var vm = new ReviewPanelViewModel(service);

        service.DiscardAll();

        Assert.Empty(vm.ReviewItems);
    }

    [Fact]
    public void ReviewItems_PreservesTintClassFromCategory()
    {
        var service = new PendingChangesService();
        service.Stage(CreateTestChange(category: ChangeCategory.Disable));
        var vm = new ReviewPanelViewModel(service);

        Assert.Equal("pending-disable", vm.ReviewItems[0].TintClass);
    }
}
