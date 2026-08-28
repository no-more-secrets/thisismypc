using ThisIsMyPC.Core.Changes;
using ThisIsMyPC.Core.Services;
using ThisIsMyPC.Modules.Startup.Changes;
using ThisIsMyPC.Modules.Startup.Models;

namespace ThisIsMyPC.Modules.Startup.Tests.Changes;

public class ServiceChangeFactoryTests
{
    private static ServiceEntry MakeEntry(ServiceStartType startType = ServiceStartType.Automatic) => new()
    {
        ServiceName = "Spooler",
        DisplayName = "Print Spooler",
        State = ServiceState.Running,
        StartType = startType,
    };

    [Fact]
    public void CreateStartTypeChange_PopulatesDescriptor()
    {
        var change = ServiceChangeFactory.CreateStartTypeChange(MakeEntry(), ServiceStartType.Disabled);

        Assert.Equal("Startup & Services", change.ModuleId);
        Assert.Equal("service-starttype:Spooler", change.SettingId);
        Assert.Equal("Spooler", change.SystemLocation);
        Assert.Equal("Automatic", change.BeforeValue);
        Assert.Equal("Disabled", change.AfterValue);
        Assert.Equal(ChangeValueType.Service_StartType, change.ValueType);
    }

    [Theory]
    [InlineData(ServiceStartType.Automatic, ServiceStartType.Disabled, ChangeCategory.Disable)]
    [InlineData(ServiceStartType.Disabled, ServiceStartType.Manual, ChangeCategory.Enable)]
    [InlineData(ServiceStartType.Automatic, ServiceStartType.Manual, ChangeCategory.Modify)]
    [InlineData(ServiceStartType.Manual, ServiceStartType.AutomaticDelayed, ChangeCategory.Modify)]
    public void CreateStartTypeChange_MapsCategory(ServiceStartType before, ServiceStartType after, ChangeCategory expected)
    {
        Assert.Equal(expected, ServiceChangeFactory.CreateStartTypeChange(MakeEntry(before), after).Category);
    }

    [Fact]
    public void Describe_HumanReadableDelayed()
    {
        Assert.Equal("Automatic (Delayed)", ServiceChangeFactory.Describe(ServiceStartType.AutomaticDelayed));
        var change = ServiceChangeFactory.CreateStartTypeChange(MakeEntry(), ServiceStartType.AutomaticDelayed);
        Assert.Equal("AutomaticDelayed", change.AfterValue); // enum name round-trips via Enum.TryParse
        Assert.Equal("Automatic (Delayed)", change.AfterDisplay);
    }
}
