using ThisIsMyPC.Core.Services;

namespace ThisIsMyPC.Core.Tests.Services;

public sealed class ServiceControlTypeTests
{
    [Fact]
    public void ServiceStatusInfo_UsesValueEquality()
    {
        var a = new ServiceStatusInfo("DiagTrack", "Connected User Experiences and Telemetry",
            ServiceState.Running, ServiceStartType.Automatic);
        var b = new ServiceStatusInfo("DiagTrack", "Connected User Experiences and Telemetry",
            ServiceState.Running, ServiceStartType.Automatic);

        Assert.Equal(a, b);
    }

    [Fact]
    public void ServiceStartType_HasExactlyTheFourSettableTypes()
    {
        Assert.Equal(
            new[] { ServiceStartType.Automatic, ServiceStartType.AutomaticDelayed, ServiceStartType.Manual, ServiceStartType.Disabled },
            Enum.GetValues<ServiceStartType>());
    }

    [Fact]
    public void ServiceState_CoversAllScmStates()
    {
        Assert.Equal(7, Enum.GetValues<ServiceState>().Length);
    }
}
