using ThisIsMyPC.Core.Modules;

namespace ThisIsMyPC.Core.Tests.Modules;

public sealed class WindowsSkuTests
{
    [Fact]
    public void All_four_sku_values_exist()
    {
        var values = Enum.GetValues<WindowsSku>();

        Assert.Equal(4, values.Length);
        Assert.Contains(WindowsSku.Home, values);
        Assert.Contains(WindowsSku.Pro, values);
        Assert.Contains(WindowsSku.Enterprise, values);
        Assert.Contains(WindowsSku.Education, values);
    }

    [Fact]
    public void All_sku_values_are_distinct()
    {
        var values = Enum.GetValues<WindowsSku>();

        Assert.Equal(values.Length, values.Distinct().Count());
    }

    [Fact]
    public void Tiers_are_home_pro_then_enterprise_education_together()
    {
        Assert.Equal(0, WindowsSku.Home.Tier());
        Assert.Equal(1, WindowsSku.Pro.Tier());
        Assert.Equal(2, WindowsSku.Enterprise.Tier());
        Assert.Equal(2, WindowsSku.Education.Tier());
    }
}
