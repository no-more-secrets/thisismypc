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
        registry.SetDWord(CurrentVersion, "CurrentMajorVersionNumber", 10);
        registry.SetString(CurrentVersion, "DisplayVersion", "24H2");
        registry.SetString(CurrentVersion, "CurrentBuildNumber", "26200");
        registry.SetDWord(CurrentVersion, "UBR", 1234);
        registry.SetString(Processor, "ProcessorNameString", "  AMD Ryzen 9 7940HS  ");
        registry.SetString(DisplayAdapter, "DriverDesc", "Removed stale display adapter");
        registry.WriteMultiString(DisplayAdapter, "InstalledDisplayDrivers", ["stale.dll"]);
        registry.SetString(@"HKLM\HARDWARE\DESCRIPTION\System\BIOS", "SystemManufacturer", "Framework");
        registry.SetString(@"HKLM\HARDWARE\DESCRIPTION\System\BIOS", "SystemProductName", "Laptop 16");

        var identity = new SystemIdentityService(
            registry,
            new FakeMemoryProvider(32UL * 1024 * 1024 * 1024),
            new FakeGpuProvider(["NVIDIA GeForce RTX 4060", "AMD Radeon Graphics"])).Read();

        Assert.Equal(Environment.MachineName, identity.MachineName);
        Assert.Equal("Windows 11 Education", identity.WindowsEdition);
        Assert.Equal("24H2 (OS build 26200.1234)", identity.WindowsVersion);
        Assert.Equal("AMD Ryzen 9 7940HS", identity.Cpu); // trimmed
        Assert.Equal("NVIDIA GeForce RTX 4060; AMD Radeon Graphics", identity.Gpu);
        Assert.DoesNotContain("Removed", identity.Gpu, StringComparison.Ordinal);
        Assert.Equal("32 GB", identity.Ram);
        Assert.Equal("Framework", identity.Manufacturer);
        Assert.Equal("Laptop 16", identity.Model);
        Assert.Contains("operating system", identity.SystemType, StringComparison.Ordinal);
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

        Assert.Equal("OS build 26200", identity.WindowsVersion);
    }

    [Fact]
    public void Windows11Build_CorrectsLegacyWindows10ProductName()
    {
        var registry = new FakeRegistryService();
        registry.SetString(CurrentVersion, "ProductName", "Windows 10 Pro");
        registry.SetDWord(CurrentVersion, "CurrentMajorVersionNumber", 10);
        registry.SetString(CurrentVersion, "CurrentBuildNumber", "22631");

        var identity = new SystemIdentityService(registry).Read();

        Assert.Equal("Windows 11 Pro", identity.WindowsEdition);
    }

    private sealed class FakeMemoryProvider(ulong? bytes) : IInstalledMemoryProvider
    {
        public ulong? GetInstalledMemoryBytes() => bytes;
    }

    private sealed class FakeGpuProvider(IReadOnlyList<string> adapters) : IGpuIdentityProvider
    {
        public IReadOnlyList<string> GetCurrentAdapterNames() => adapters;
    }
}
