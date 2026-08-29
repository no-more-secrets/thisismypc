using ThisIsMyPC.Core.Services;
using ThisIsMyPC.Core.Tests.Fakes;

namespace ThisIsMyPC.Core.Tests.Services;

public sealed class SystemIdentityServiceTests
{
    private const string CurrentVersion = @"HKLM\SOFTWARE\Microsoft\Windows NT\CurrentVersion";
    private const string Processor = @"HKLM\HARDWARE\DESCRIPTION\System\CentralProcessor\0";
    private const string DisplayAdapter =
        @"HKLM\SYSTEM\CurrentControlSet\Control\Class\{4d36e968-e325-11ce-bfc1-08002be10318}\0000";

    [Fact]
    public void FullRegistryData_MapsAllFields()
    {
        var registry = new FakeRegistryService();
        registry.SetString(CurrentVersion, "ProductName", "Windows 11 Education");
        registry.SetString(CurrentVersion, "DisplayVersion", "24H2");
        registry.SetString(CurrentVersion, "CurrentBuildNumber", "26200");
        registry.SetString(Processor, "ProcessorNameString", "  AMD Ryzen 9 7940HS  ");
        registry.SetString(DisplayAdapter, "DriverDesc", "NVIDIA GeForce RTX 4060");

        var identity = new SystemIdentityService(registry).Read();

        Assert.Equal(Environment.MachineName, identity.MachineName);
        Assert.Equal("Windows 11 Education", identity.WindowsEdition);
        Assert.Equal("24H2 (build 26200)", identity.WindowsVersion);
        Assert.Equal("AMD Ryzen 9 7940HS", identity.Cpu); // trimmed
        Assert.Equal("NVIDIA GeForce RTX 4060", identity.Gpu);
        Assert.EndsWith(" GB", identity.Ram, StringComparison.Ordinal);
    }

    [Fact]
    public void EmptyRegistry_DegradesToUnknown_NeverThrows()
    {
        var identity = new SystemIdentityService(new FakeRegistryService()).Read();

        Assert.Equal("Unknown", identity.WindowsEdition);
        Assert.Equal("Unknown", identity.WindowsVersion);
        Assert.Equal("Unknown", identity.Cpu);
        Assert.Equal("Unknown", identity.Gpu);
        Assert.NotEmpty(identity.MachineName);
    }

    [Fact]
    public void PartialVersionData_FormatsWhatExists()
    {
        var registry = new FakeRegistryService();
        registry.SetString(CurrentVersion, "CurrentBuildNumber", "26200");

        var identity = new SystemIdentityService(registry).Read();

        Assert.Equal("build 26200", identity.WindowsVersion);
    }
}
