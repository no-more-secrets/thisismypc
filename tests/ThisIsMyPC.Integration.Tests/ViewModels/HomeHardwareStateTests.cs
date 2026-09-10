using ThisIsMyPC.App.ViewModels;
using ThisIsMyPC.Core.Hardware;
using ThisIsMyPC.Core.Services;
using ThisIsMyPC.Integration.Tests.Fakes;

namespace ThisIsMyPC.Integration.Tests.ViewModels;

public sealed class HomeHardwareStateTests
{
    [Fact]
    public async Task LateScanCannotReplaceDisposedHomeIdentity()
    {
        var pending = new TaskCompletionSource<HardwareSnapshot>();
        var service = new FakeHardware(() => pending.Task);
        var vm = Create(service);
        var loading = vm.LoadHardwareAsync();
        Assert.False(vm.CanRefreshHardware);
        vm.Dispose();
        vm.Dispose();
        pending.SetResult(Snapshot());
        await loading;
        Assert.Equal("Original", vm.Identity.Manufacturer);
        Assert.False(vm.CanRefreshHardware);
    }

    [Fact]
    public async Task FailureAllowsRefreshAndAllPresentGraphicsReachHome()
    {
        var fails = true;
        var service = new FakeHardware(() => fails ? Task.FromException<HardwareSnapshot>(new IOException("Unavailable")) : Task.FromResult(Snapshot()));
        using var vm = Create(service);
        await vm.LoadHardwareAsync();
        Assert.Equal("Hardware details are unavailable.", vm.HardwareStatus);
        Assert.True(vm.CanRefreshHardware);
        fails = false;
        await vm.RefreshHardwareCommand.ExecuteAsync(null);
        Assert.Equal(1, service.Refreshes);
        Assert.Equal("ASUS", vm.Identity.Manufacturer);
        Assert.Equal("Integrated GPU; Discrete GPU", vm.Identity.Gpu);
        Assert.Equal("Laptop", vm.FormFactorSummary);
        Assert.Equal("", vm.HardwareStatus);
    }

    private static HardwareSnapshot Snapshot() => new()
    {
        Facts = new() { Identity = MachineIdentity.From("ASUS", "Test laptop"), FormFactor = new() { SmbiosChassisTypes = [10] } },
        Devices = [new("Integrated GPU", "Display", []), new("Discrete GPU", "Display", [])],
    };

    private static HomeViewModel Create(IHardwareDetectionService service) => new(new SystemIdentity
    {
        MachineName = "Test", Manufacturer = "Original", Model = "Unknown", Cpu = "CPU", Gpu = "GPU", Ram = "32 GB",
        WindowsEdition = "Windows 11", WindowsVersion = "Test", SystemType = "x64",
    }, new FakeChangeHistoryService(), hardwareDetection: service);

    private sealed class FakeHardware(Func<Task<HardwareSnapshot>> read) : IHardwareDetectionService
    {
        public int Refreshes { get; private set; }
        public Task<HardwareSnapshot> GetSnapshotAsync(CancellationToken cancellationToken = default) => read();
        public Task<HardwareSnapshot> RefreshAsync(CancellationToken cancellationToken = default) { Refreshes++; return read(); }
    }
}
