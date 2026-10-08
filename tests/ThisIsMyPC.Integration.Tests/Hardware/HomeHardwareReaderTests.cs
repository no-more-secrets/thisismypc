using System.Buffers.Binary;
using System.Text;
using ThisIsMyPC.Interop.Win32.Hardware;
namespace ThisIsMyPC.Integration.Tests.Hardware;
public sealed class HomeHardwareReaderTests
{
    [Fact]
    [Trait("Category", "Diagnostic")]
    public void LiveInventoryReadsWithoutExecutingInstalledPrograms()
    {
        var graphics = HomeHardwareReader.ReadGraphics();
        var storage = HomeHardwareReader.ReadStorage();
        var software = ThisIsMyPC.Interop.Win32.Packages.SupplementalSoftwareInventory.Read();
        Assert.Contains(graphics, g => !g.IsSoftware && g.DedicatedVideoMemoryBytes > 0);
        Assert.Contains(storage, d => d.IsBootDrive == true);
        Assert.Contains(software, p => p.PackageId == "Anthropic.ClaudeCode" && !p.CanUninstall);
    }

    [Fact]
    public void DescriptorUsesBoundedVendorAndProductOffsets()
    {
        var bytes = new byte[100];
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(12), 36);
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(16), 44);
        Encoding.ASCII.GetBytes("Samsung").CopyTo(bytes, 36);
        Encoding.ASCII.GetBytes("Samsung SSD 990 PRO").CopyTo(bytes, 44);
        Assert.Equal("Samsung SSD 990 PRO", HomeHardwareReader.ReadStorageName(bytes));
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(16), uint.MaxValue);
        Assert.Equal("Samsung", HomeHardwareReader.ReadStorageName(bytes));
        Assert.Null(HomeHardwareReader.ReadStorageName(bytes.AsSpan(0, 20)));
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(12), 12);
        Assert.Null(HomeHardwareReader.ReadStorageName(bytes));
    }
}
