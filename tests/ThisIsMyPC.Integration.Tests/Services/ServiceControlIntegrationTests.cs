using ThisIsMyPC.Core.Results;
using ThisIsMyPC.Core.Services;
using ThisIsMyPC.Interop.Win32.Services;

namespace ThisIsMyPC.Integration.Tests.Services;

/// <summary>
/// Read-only SCM queries against well-known services. No mutations — these tests
/// must never change service state or configuration on the machine running them.
/// </summary>
[Trait("Category", "Integration")]
public sealed class ServiceControlIntegrationTests
{
    private readonly ServiceControlService _sut = new();

    [Fact]
    public void Query_EventLog_ReturnsRunningAutomatic()
    {
        // The Windows Event Log service exists on every supported Windows install,
        // is Automatic, and is effectively always running.
        var result = _sut.Query("EventLog");

        Assert.True(result.IsSuccess, result.ErrorMessage);
        Assert.Equal("EventLog", result.Value!.ServiceName);
        Assert.False(string.IsNullOrWhiteSpace(result.Value.DisplayName));
        Assert.Equal(ServiceState.Running, result.Value.State);
        Assert.Equal(ServiceStartType.Automatic, result.Value.StartType);
    }

    [Fact]
    public void Query_NonexistentService_ReturnsNotFound()
    {
        var result = _sut.Query("ThisIsMyPC_NoSuchService_9f3a");

        Assert.False(result.IsSuccess);
        Assert.Equal(ErrorCategory.NotFound, result.ErrorCategory);
    }

    [Fact]
    public void Query_DelayedAutoStartFlag_MatchesRegistry()
    {
        // Self-calibrating: read sppsvc's actual DelayedAutostart registry value and
        // assert the SCM-derived start type agrees, so a broken delayed-flag read
        // fails regardless of how this machine is configured.
        var result = _sut.Query("sppsvc");
        Assert.True(result.IsSuccess, result.ErrorMessage);

        var registry = new ThisIsMyPC.Interop.Win32.Registry.RegistryService();
        var delayed = registry.ReadDWord(@"HKLM\SYSTEM\CurrentControlSet\Services\sppsvc", "DelayedAutostart");
        var start = registry.ReadDWord(@"HKLM\SYSTEM\CurrentControlSet\Services\sppsvc", "Start");

        Assert.True(start.IsSuccess, start.ErrorMessage);
        if (start.Value == 2) // SERVICE_AUTO_START
        {
            var expectDelayed = delayed.IsSuccess && delayed.Value == 1;
            Assert.Equal(
                expectDelayed ? ServiceStartType.AutomaticDelayed : ServiceStartType.Automatic,
                result.Value!.StartType);
        }
        else
        {
            Assert.NotEqual(ServiceStartType.AutomaticDelayed, result.Value!.StartType);
        }
    }

    [Fact]
    public void Query_StoppedManualService_ReportsState()
    {
        // Fax or another rarely-running service isn't guaranteed present; use a service
        // guaranteed to exist whose state we don't assert — only that the read succeeds
        // and returns a coherent record.
        var result = _sut.Query("WSearch");

        if (result.IsSuccess)
        {
            Assert.Equal("WSearch", result.Value!.ServiceName);
        }
        else
        {
            Assert.Equal(ErrorCategory.NotFound, result.ErrorCategory);
        }
    }
}
