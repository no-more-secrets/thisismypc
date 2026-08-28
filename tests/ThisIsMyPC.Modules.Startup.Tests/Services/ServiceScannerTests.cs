using ThisIsMyPC.Core.Results;
using ThisIsMyPC.Core.Services;
using ThisIsMyPC.Modules.Startup.Services;
using ThisIsMyPC.Modules.Startup.Tests.Fakes;

namespace ThisIsMyPC.Modules.Startup.Tests.Services;

public class ServiceScannerTests
{
    private readonly FakeServiceControlService _services = new();

    [Fact]
    public void Scan_ReturnsAllServicesWithDetails()
    {
        _services.AddService("Spooler", ServiceState.Running, ServiceStartType.Automatic,
            displayName: "Print Spooler", description: "Manages print jobs");

        var entries = new ServiceScanner(_services).Scan();

        var entry = Assert.Single(entries);
        Assert.Equal("Spooler", entry.ServiceName);
        Assert.Equal("Print Spooler", entry.DisplayName);
        Assert.Equal("Manages print jobs", entry.Description);
        Assert.Equal(ServiceState.Running, entry.State);
        Assert.Equal(ServiceStartType.Automatic, entry.StartType);
        Assert.False(entry.IsPerUserInstance);
    }

    [Fact]
    public void Scan_PerUserInstance_GroupedWithTemplate()
    {
        _services.AddService("CDPUserSvc", ServiceState.Stopped, ServiceStartType.Manual);
        _services.AddService("CDPUserSvc_5f3a2", ServiceState.Running, ServiceStartType.Manual);

        var entries = new ServiceScanner(_services).Scan();

        var instance = entries.Single(e => e.ServiceName == "CDPUserSvc_5f3a2");
        Assert.True(instance.IsPerUserInstance);
        Assert.Equal("CDPUserSvc", instance.TemplateServiceName);

        var template = entries.Single(e => e.ServiceName == "CDPUserSvc");
        Assert.False(template.IsPerUserInstance);
        Assert.Null(template.TemplateServiceName);
    }

    [Fact]
    public void Scan_HexSuffixWithoutTemplate_NotMarkedPerUser()
    {
        // No "Foo" template service exists — suffix alone doesn't qualify
        _services.AddService("Foo_abcd", ServiceState.Running, ServiceStartType.Manual);

        var entries = new ServiceScanner(_services).Scan();

        Assert.False(Assert.Single(entries).IsPerUserInstance);
    }

    [Fact]
    public void Scan_NonHexSuffix_NotMarkedPerUser()
    {
        _services.AddService("MyService", ServiceState.Running, ServiceStartType.Manual);
        _services.AddService("MyService_backup", ServiceState.Running, ServiceStartType.Manual);

        var entries = new ServiceScanner(_services).Scan();

        Assert.False(entries.Single(e => e.ServiceName == "MyService_backup").IsPerUserInstance);
    }

    [Fact]
    public void Scan_EnumerateFailure_ReturnsEmpty()
    {
        _services.InjectFailure("EnumerateAll", "*", ErrorCategory.AccessDenied);

        Assert.Empty(new ServiceScanner(_services).Scan());
    }
}
