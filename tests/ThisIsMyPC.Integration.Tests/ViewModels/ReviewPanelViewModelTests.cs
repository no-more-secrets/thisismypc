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
    public void ReviewGroups_PopulatesFromPendingChangesService()
    {
        var service = new PendingChangesService();
        service.Stage(CreateTestChange());
        var vm = new ReviewPanelViewModel(service);

        Assert.Single(vm.ReviewGroups);
        Assert.Equal("Test Setting", vm.ReviewGroups[0].DisplayName);
        Assert.Equal("Disabled", vm.ReviewGroups[0].BeforeDisplay);
        Assert.Equal("Enabled", vm.ReviewGroups[0].AfterDisplay);

        // Detail items still accessible
        Assert.Single(vm.ReviewGroups[0].Details);
        Assert.Equal(@"HKLM\Test", vm.ReviewGroups[0].Details[0].SystemLocation);
    }

    [Fact]
    public void ReviewGroups_UpdatesWhenChangeStaged()
    {
        var service = new PendingChangesService();
        var vm = new ReviewPanelViewModel(service);

        Assert.Empty(vm.ReviewGroups);

        service.Stage(CreateTestChange());

        Assert.Single(vm.ReviewGroups);
    }

    [Fact]
    public void ReviewGroups_ClearsWhenDiscardAll()
    {
        var service = new PendingChangesService();
        service.Stage(CreateTestChange());
        var vm = new ReviewPanelViewModel(service);

        service.DiscardAll();

        Assert.Empty(vm.ReviewGroups);
    }

    [Fact]
    public void ReviewGroups_PreservesCategoryFromPrimaryChange()
    {
        var service = new PendingChangesService();
        service.Stage(CreateTestChange(category: ChangeCategory.Disable));
        var vm = new ReviewPanelViewModel(service);

        Assert.True(vm.ReviewGroups[0].IsDisableOrDelete);
    }

    [Fact]
    public void ReviewGroups_MultiChangeGroupShowsAllDetails()
    {
        var service = new PendingChangesService();
        var group = new ChangeGroup
        {
            GroupId = "g1",
            DisplayName = "Context menu: 7-Zip",
            Description = "Toggle 7-Zip",
            Changes =
            [
                new ChangeDescriptor
                {
                    ModuleId = "shell", SettingId = "s1",
                    DisplayName = "Context menu: 7-Zip",
                    SystemLocation = @"HKLM\...\Blocked\{clsid}",
                    BeforeValue = "__absent__", AfterValue = "",
                    BeforeDisplay = "Enabled", AfterDisplay = "Disabled",
                    ValueType = ChangeValueType.Registry_String,
                    Category = ChangeCategory.Disable,
                },
                new ChangeDescriptor
                {
                    ModuleId = "shell", SettingId = "s2",
                    DisplayName = "Context menu: 7-Zip",
                    SystemLocation = @"HKCR\*\shellex\7-Zip",
                    BeforeValue = "{clsid}", AfterValue = "-{clsid}",
                    BeforeDisplay = "Enabled", AfterDisplay = "Disabled",
                    ValueType = ChangeValueType.Registry_String,
                    Category = ChangeCategory.Disable,
                },
            ],
        };
        service.Stage(group);
        var vm = new ReviewPanelViewModel(service);

        Assert.Single(vm.ReviewGroups);
        var reviewGroup = vm.ReviewGroups[0];
        Assert.Equal("Context menu: 7-Zip", reviewGroup.DisplayName);
        Assert.Equal(2, reviewGroup.DetailCount);
        Assert.True(reviewGroup.HasMultipleDetails);
        Assert.False(reviewGroup.IsExpanded);
    }
}
