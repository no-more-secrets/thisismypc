using ThisIsMyPC.Core.Services;
using ThisIsMyPC.Interop.Win32;

namespace ThisIsMyPC.Integration.Tests.Services;

public sealed class ExplorerRestartServiceTests
{
    [Fact]
    public void Service_implements_IExplorerRestartService()
    {
        var service = new ExplorerRestartService();

        Assert.IsAssignableFrom<IExplorerRestartService>(service);
    }

    [Fact]
    public void Fake_service_default_succeeds()
    {
        var fake = new Fakes.FakeExplorerRestartService();

        Assert.True(fake.ShouldSucceed);
        Assert.False(fake.WasCalled);
    }

    [Fact]
    public async Task Fake_service_tracks_call()
    {
        var fake = new Fakes.FakeExplorerRestartService();

        var result = await fake.RestartExplorerAsync();

        Assert.True(fake.WasCalled);
        Assert.True(result.IsSuccess);
    }

    [Fact]
    public async Task Fake_service_can_simulate_failure()
    {
        var fake = new Fakes.FakeExplorerRestartService { ShouldSucceed = false };

        var result = await fake.RestartExplorerAsync();

        Assert.False(result.IsSuccess);
        Assert.Equal("Simulated restart failure", result.ErrorMessage);
    }
}
