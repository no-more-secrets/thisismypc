using System.Buffers.Binary;
using System.Text;
using ThisIsMyPC.Core.Hardware;

namespace ThisIsMyPC.Core.Tests.Hardware;

public sealed class HardwareInventoryTests
{
    [Fact]
    public void FirmwareSeparatesSystemBoardAndChassisAndReadsConfiguredMemory()
    {
        var system = Structure(1, 8, ["ASUSTeK COMPUTER INC.", "System Product Name"], (4, 1), (5, 2));
        var board = Structure(2, 15, ["ASUSTeK COMPUTER INC.", "ROG STRIX B550-F GAMING (WI-FI)", "Rev X.0x"], (4, 1), (5, 2), (6, 3), (13, 10));
        var chassis = Structure(3, 9, [], (5, 0x83));
        var memory = Structure(17, 34, ["DIMM_A2"], (16, 1), (18, 0x1a));
        BinaryPrimitives.WriteUInt16LittleEndian(memory.AsSpan(12), 16384);
        BinaryPrimitives.WriteUInt16LittleEndian(memory.AsSpan(32), 3200);
        var result = SmbiosInventoryParser.Parse([..system, ..board, ..chassis, ..memory]);
        Assert.Equal("ASUSTeK COMPUTER INC.", result.Manufacturer);
        Assert.Null(result.Model);
        Assert.Equal("ROG STRIX B550-F GAMING (WI-FI)", result.BoardProduct);
        Assert.Equal([3], result.ChassisTypes);
        Assert.Equal(new("DIMM_A2", 16UL * 1024 * 1024 * 1024, "DDR4", 3200), Assert.Single(result.MemoryDevices));
    }

    [Fact]
    public void ShortStructuresAndDaughterboardsDoNotInventMotherboardOrMemory()
    {
        var daughter = Structure(2, 15, ["Wrong board"], (5, 1), (13, 6));
        var shortMemory = Structure(17, 4, []);
        var result = SmbiosInventoryParser.Parse([..daughter, ..shortMemory]);
        Assert.Null(result.BoardProduct);
        Assert.Empty(result.MemoryDevices);
    }

    [Theory]
    [InlineData(0, 0UL)]
    [InlineData(0x8001, 1024UL)]
    [InlineData(1024, 1073741824UL)]
    public void MemorySizeUnitsArePreserved(int value, ulong expected)
    {
        var memory = Structure(17, 21, []);
        BinaryPrimitives.WriteUInt16LittleEndian(memory.AsSpan(12), (ushort)value);
        Assert.Equal(expected, Assert.Single(SmbiosInventoryParser.Parse(memory).MemoryDevices).SizeBytes);
    }

    [Fact]
    public void ExtendedSizeAndUnknownSpeedAreHandled()
    {
        var memory = Structure(17, 92, []);
        BinaryPrimitives.WriteUInt16LittleEndian(memory.AsSpan(12), 0x7fff);
        BinaryPrimitives.WriteUInt32LittleEndian(memory.AsSpan(28), 65536);
        BinaryPrimitives.WriteUInt16LittleEndian(memory.AsSpan(32), 0xffff);
        BinaryPrimitives.WriteUInt32LittleEndian(memory.AsSpan(88), 7200);
        var result = Assert.Single(SmbiosInventoryParser.Parse(memory).MemoryDevices);
        Assert.Equal(64UL * 1024 * 1024 * 1024, result.SizeBytes);
        Assert.Equal(7200U, result.ConfiguredSpeedMt);
        BinaryPrimitives.WriteUInt16LittleEndian(memory.AsSpan(12), 0xffff);
        BinaryPrimitives.WriteUInt16LittleEndian(memory.AsSpan(32), 0);
        result = Assert.Single(SmbiosInventoryParser.Parse(memory).MemoryDevices);
        Assert.Null(result.SizeBytes);
        Assert.Null(result.ConfiguredSpeedMt);
    }

    [Fact]
    public void TruncatedAndUnterminatedTablesAreRejected()
    {
        Assert.Throws<InvalidDataException>(() => SmbiosInventoryParser.Parse([1, 20, 0, 0]));
        Assert.Throws<InvalidDataException>(() => SmbiosInventoryParser.Parse([1, 4, 0, 0, 65, 0]));
        Assert.Throws<InvalidDataException>(() => SmbiosInventoryParser.Parse([1, 0, 0, 0, 0, 0]));
    }

    [Theory]
    [InlineData("ROG STRIX B550-F GAMING (WI-FI)", "B550")]
    [InlineData("PRO B650M-A WIFI", "B650")]
    [InlineData("ROG STRIX X670E-I GAMING WIFI", "X670E")]
    [InlineData("ROG Zephyrus G14 GA401", null)]
    [InlineData("AB5500", null)]
    public void BoardInferenceIsLabeledAndBounded(string model, string? expected)
    {
        var result = ChipsetIdentityResolver.Resolve(model, []);
        Assert.Equal(expected, result.Name);
        if (expected is not null) Assert.Equal("Inferred from motherboard model", result.Source);
    }

    [Fact]
    public void NamedPciChipsetWinsButGenericBridgesAndConflictsDoNotGuess()
    {
        HardwareDevice Device(string name) => new(name, "System", [@"PCI\VEN_8086&DEV_0000"]);
        Assert.Equal("Z790", ChipsetIdentityResolver.Resolve("B550", [Device("Intel Z790 LPC Controller")]).Name);
        Assert.Null(ChipsetIdentityResolver.Resolve(null, [Device("AMD 500 Series Chipset PCIe Bridge")]).Name);
        Assert.Null(ChipsetIdentityResolver.Resolve("B550", [Device("Intel Z790 LPC"), Device("Intel Z690 LPC")]).Name);
    }

    private static byte[] Structure(byte type, byte length, string[] strings, params (int Offset, byte Value)[] fields)
    {
        var data = new byte[length]; data[0] = type; data[1] = length;
        foreach (var (offset, value) in fields) data[offset] = value;
        return [..data, ..Encoding.UTF8.GetBytes(string.Join('\0', strings)), 0, 0];
    }
}
