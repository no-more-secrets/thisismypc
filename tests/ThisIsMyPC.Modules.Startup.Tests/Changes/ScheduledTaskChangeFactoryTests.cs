using ThisIsMyPC.Core.Changes;
using ThisIsMyPC.Modules.Startup.Changes;
using ThisIsMyPC.Modules.Startup.Models;

namespace ThisIsMyPC.Modules.Startup.Tests.Changes;

public class ScheduledTaskChangeFactoryTests
{
    private static ScheduledTaskEntry MakeEntry(bool enabled = true) => new()
    {
        Name = "Consolidator",
        Path = @"\Microsoft\Windows\Customer Experience Improvement Program\Consolidator",
        IsEnabled = enabled,
        Classification = TaskClassification.Telemetry,
    };

    [Fact]
    public void CreateToggle_Disable_PopulatesDescriptor()
    {
        var change = ScheduledTaskChangeFactory.CreateToggle(MakeEntry(), enable: false);

        Assert.Equal("Startup & Services", change.ModuleId);
        Assert.Equal(@"\Microsoft\Windows\Customer Experience Improvement Program\Consolidator", change.SystemLocation);
        Assert.Equal("Enabled", change.BeforeValue);
        Assert.Equal("Disabled", change.AfterValue);
        Assert.Equal(ChangeValueType.ScheduledTask_State, change.ValueType);
        Assert.Equal(ChangeCategory.Disable, change.Category);
    }

    [Fact]
    public void CreateToggle_Enable_FromDisabled()
    {
        var change = ScheduledTaskChangeFactory.CreateToggle(MakeEntry(enabled: false), enable: true);

        Assert.Equal("Disabled", change.BeforeValue);
        Assert.Equal("Enabled", change.AfterValue);
        Assert.Equal(ChangeCategory.Enable, change.Category);
    }
}
