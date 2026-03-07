using ThisIsMyPC.App.ViewModels;
using ThisIsMyPC.Core.Changes;

namespace ThisIsMyPC.Integration.Tests.ViewModels;

public class ReviewItemViewModelTests
{
    private static ReviewItemViewModel CreateItem(ChangeCategory category) => new()
    {
        DisplayName = "Test",
        Description = "Test",
        SystemLocation = @"HKLM\Test",
        BeforeDisplay = "Off",
        AfterDisplay = "On",
        Category = category,
        GroupId = "g1",
        SettingId = "s1",
    };

    [Theory]
    [InlineData(ChangeCategory.Enable, "pending-enable")]
    [InlineData(ChangeCategory.Create, "pending-enable")]
    [InlineData(ChangeCategory.Disable, "pending-disable")]
    [InlineData(ChangeCategory.Delete, "pending-disable")]
    [InlineData(ChangeCategory.Modify, "pending-modify")]
    public void TintClass_ReturnsCorrectClassForCategory(ChangeCategory category, string expected)
    {
        var item = CreateItem(category);
        Assert.Equal(expected, item.TintClass);
    }

    [Fact]
    public void IsIncluded_DefaultsToTrue()
    {
        var item = CreateItem(ChangeCategory.Enable);
        Assert.True(item.IsIncluded);
    }
}
