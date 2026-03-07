using ThisIsMyPC.Core.Changes;

namespace ThisIsMyPC.Core.Tests.Changes;

public class ChangeGroupTests
{
    private static ChangeDescriptor CreateTestChange(string settingId = "test") => new()
    {
        ModuleId = "TestModule",
        SettingId = settingId,
        DisplayName = "Test Change",
        SystemLocation = "HKCU\\Test",
        BeforeValue = "0",
        AfterValue = "1",
        BeforeDisplay = "Off",
        AfterDisplay = "On",
        ValueType = ChangeValueType.Registry_DWord,
        Category = ChangeCategory.Modify
    };

    [Fact]
    public void ChangeGroup_ContainsOrderedListOfChangeDescriptors()
    {
        var change1 = CreateTestChange("first");
        var change2 = CreateTestChange("second");
        var change3 = CreateTestChange("third");

        var group = new ChangeGroup
        {
            GroupId = "g1",
            DisplayName = "Test Group",
            Description = "Ordered group",
            Changes = [change1, change2, change3]
        };

        Assert.Equal(3, group.Changes.Count);
        Assert.Equal("first", group.Changes[0].SettingId);
        Assert.Equal("second", group.Changes[1].SettingId);
        Assert.Equal("third", group.Changes[2].SettingId);
    }

    [Fact]
    public void StandaloneChange_TreatedAsGroupOfOne()
    {
        var change = CreateTestChange();

        var group = new ChangeGroup
        {
            GroupId = "single",
            DisplayName = change.DisplayName,
            Description = change.DisplayName,
            Changes = [change]
        };

        Assert.Single(group.Changes);
        Assert.Equal(change, group.Changes[0]);
    }
}
