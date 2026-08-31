using ThisIsMyPC.Core.Services;
using ThisIsMyPC.Modules.Display.Services;
using ThisIsMyPC.Modules.Display.Tests.Fakes;

namespace ThisIsMyPC.Modules.Display.Tests;

public sealed class InternalPanelServiceTests
{
    private static readonly Guid ActivePlan = Guid.NewGuid();

    private static PowerSettingInfo BrightnessSetting(uint ac = 70, uint dc = 40) => new(
        InternalPanelService.VideoSubgroup, "Display",
        InternalPanelService.BrightnessSetting, "Display brightness",
        null, ac, dc, "%", IsRange: true, Min: 0, Max: 100, Increment: 1, PossibleValues: []);

    private static FakePowerService PowerWithPanel()
    {
        var power = new FakePowerService();
        power.AddPlan(ActivePlan, "Balanced", isActive: true);
        power.AddSetting(BrightnessSetting());
        return power;
    }

    [Fact]
    public void ReadPanel_ReturnsTheBrightnessOfTheActivePlan()
    {
        var panel = new InternalPanelService(PowerWithPanel()).ReadPanel();

        Assert.NotNull(panel);
        Assert.True(panel.IsInternalPanel);
        Assert.Equal(70, panel.Brightness);
        Assert.Equal(100, panel.BrightnessMax);
        Assert.Null(panel.Contrast);
    }

    [Fact]
    public void ReadPanel_NullWhenTheSettingIsAbsent()
    {
        var power = new FakePowerService();
        power.AddPlan(ActivePlan, "Balanced", isActive: true);

        Assert.Null(new InternalPanelService(power).ReadPanel());
    }

    [Fact]
    public void SetBrightness_WritesAcAndDcToTheActivePlan()
    {
        var power = PowerWithPanel();

        var result = new InternalPanelService(power).SetBrightness(55);

        Assert.True(result.IsSuccess);
        var prefix = $"{ActivePlan:D}/{InternalPanelService.VideoSubgroup:D}/{InternalPanelService.BrightnessSetting:D}";
        Assert.Equal(55u, power.WrittenIndexes[$"{prefix}/AC"]);
        Assert.Equal(55u, power.WrittenIndexes[$"{prefix}/DC"]);
    }

    [Fact]
    public void SetBrightness_FailsWithoutAnActivePlan()
    {
        var power = new FakePowerService();
        power.AddPlan(ActivePlan, "Balanced", isActive: false);

        Assert.False(new InternalPanelService(power).SetBrightness(55).IsSuccess);
    }
}
