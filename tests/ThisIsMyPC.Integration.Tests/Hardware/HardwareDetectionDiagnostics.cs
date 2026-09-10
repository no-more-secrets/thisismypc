using ThisIsMyPC.Interop.Win32.Registry;
using ThisIsMyPC.Interop.Win32.Hardware;

namespace ThisIsMyPC.Integration.Tests.Hardware;

public sealed class HardwareDetectionDiagnostics
{
    [Fact]
    [Trait("Category", "Diagnostic")]
    public async Task LiveInventoryReadsWithoutElevationOrMutations()
    {
        var service = new HardwareDetectionService(new RegistryService());
        var snapshot = await service.GetSnapshotAsync();
        Assert.Same(snapshot, await service.GetSnapshotAsync());
        Assert.NotEqual(default, snapshot.ObservedAt);
        var root = FindRoot();
        var report = Path.Combine(root, "artifacts", "diagnostics", "hardware-live");
        Directory.CreateDirectory(report);
        await File.WriteAllLinesAsync(Path.Combine(report, "inventory.txt"),
        [
            $"Manufacturer: {snapshot.Firmware.Manufacturer}", $"Model: {snapshot.Firmware.Model}",
            $"Motherboard: {snapshot.Firmware.BoardManufacturer} {snapshot.Firmware.BoardProduct}",
            $"Chipset: {snapshot.Chipset.Name} ({snapshot.Chipset.Source})",
            $"Chassis: {string.Join(", ", snapshot.Firmware.ChassisTypes)}",
            $"BIOS: {snapshot.Firmware.BiosVersion} {snapshot.Firmware.BiosDate}",
            $"Memory: {string.Join("; ", snapshot.Firmware.MemoryDevices)}",
            $"Present devices: {snapshot.Devices.Count}",
            $"Graphics: {string.Join("; ", snapshot.Devices.Where(d => d.ClassName == "Display").Select(d => d.Name))}",
            $"Storage: {string.Join("; ", snapshot.Devices.Where(d => d.ClassName == "DiskDrive").Select(d => d.Name))}",
            $"Issues: {string.Join("; ", snapshot.Issues)}",
        ]);
    }

    private static string FindRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "ThisIsMyPC.slnx"))) directory = directory.Parent;
        return directory?.FullName ?? throw new DirectoryNotFoundException("Repository root not found.");
    }
}
