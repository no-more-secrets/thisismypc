using ThisIsMyPC.Core.Hardware.Lighting;
using ThisIsMyPC.Lighting.Controllers.Ene;
using ThisIsMyPC.Lighting.Tests.Fakes;
using ThisIsMyPC.Lighting.Transport;

namespace ThisIsMyPC.Lighting.Tests;

/// <summary>The ENE register protocol against a simulated chip on a GPU's I2C port.</summary>
public class EneSmBusControllerTests
{
    private const byte Address = 0x67;

    /// <summary>A "AUMA0-E6K5-0107"-style table: LED count at 0x03, two zones (PCIe with 2 LEDs, Backplate with 1) at channel offset 0x1B.</summary>
    private static byte[] StrixTable()
    {
        var table = new byte[64];
        table[0x02] = 0;
        table[0x03] = 2;
        table[0x04] = 1;
        table[0x1B] = 0x8B;
        table[0x1C] = 0x88;
        return table;
    }

    private static FakeEneChip StrixChip(byte[]? table = null)
    {
        var chip = new FakeEneChip(Address, "AUMA0-E6K5-0107", table ?? StrixTable());
        // 0x03 + zone: the controller reads LED counts per zone from the same table.
        chip[0x8021] = 1; // static
        chip[0x8022] = 2;
        chip[0x8023] = 0;
        chip[0x8020] = 0;
        // Effect colors for 3 LEDs, stored R, B, G.
        chip[0x8160] = 0xAA; chip[0x8161] = 0xCC; chip[0x8162] = 0xBB;
        chip[0x8163] = 0x01; chip[0x8164] = 0x03; chip[0x8165] = 0x02;
        chip[0x8166] = 0x10; chip[0x8167] = 0x30; chip[0x8168] = 0x20;
        return chip;
    }

    [Fact]
    public void Probe_AcceptsACountingChip_AndRejectsMicron()
    {
        var notes = new List<string>();
        Assert.True(EneSmBusController.Probe(StrixChip(), Address, notes));

        var micron = new FakeEneChip(Address, "AUMA0-E6K5-0107", StrixTable());
        foreach (var (b, i) in "Micron"u8.ToArray().Select((b, i) => (b, i)))
            micron[(ushort)(EneSmBusController.RegMicronCheck + i)] = b;
        Assert.False(EneSmBusController.Probe(micron, Address, notes));
        Assert.Contains(notes, n => n.Contains("Micron", StringComparison.Ordinal));

        Assert.False(EneSmBusController.Probe(StrixChip(), 0x40, notes));
    }

    [Fact]
    public void Constructor_ReadsNameTableZonesAndCurrentMode()
    {
        var chip = StrixChip();
        var controller = new EneSmBusController(chip, Address, "ASUS ROG STRIX GeForce RTX 4080 Gaming", LightingDeviceType.Gpu);
        var device = controller.Describe(3);

        Assert.Equal("AUMA0-E6K5-0107", controller.Version);
        Assert.Equal(3, device.Index);
        Assert.Equal(LightingDeviceType.Gpu, device.Type);
        Assert.Equal("ASUS", device.Vendor);
        Assert.Equal(2, device.Zones.Count);
        Assert.Equal("PCIe", device.Zones[0].Name);
        Assert.Equal(2u, device.Zones[0].LedCount);
        Assert.Equal(LightingZoneType.Linear, device.Zones[0].Type);
        Assert.Equal("Backplate", device.Zones[1].Name);
        Assert.Equal(3, device.Leds.Count);
        Assert.Equal("PCIe LED 2", device.Leds[1].Name);
        Assert.Equal(2, device.ActiveModeIndex);
        Assert.Equal("Static", device.ActiveMode!.Name);
        Assert.Equal(new RgbColor(0xAA, 0xBB, 0xCC), device.Colors[0]);
        Assert.Equal(new RgbColor(0x10, 0x20, 0x30), device.Colors[2]);
        Assert.Equal(10, device.Modes.Count);
        Assert.Equal((0u, 4u), device.Modes[3].SpeedRange);
        Assert.Equal(4u, device.Modes[3].SpeedMin);
        Assert.Contains("0x67", device.Location, StringComparison.Ordinal);
    }

    [Fact]
    public void ActiveMode_RandomVariant_MapsBackToItsBaseModeWithRandomColors()
    {
        var chip = StrixChip();
        chip[0x8021] = 6; // spectrum cycle breathing
        chip[0x8022] = 3;
        var device = new EneSmBusController(chip, Address, "GPU", LightingDeviceType.Gpu).Describe(0);

        Assert.Equal("Breathing", device.ActiveMode!.Name);
        Assert.Equal(LightingColorMode.Random, device.ActiveMode.ColorMode);
        Assert.Equal(3u, device.ActiveMode.Speed);
    }

    [Fact]
    public void SetLeds_InStatic_WritesColorsInBlocksOfThree_ThenApplies()
    {
        var chip = StrixChip();
        var controller = new EneSmBusController(chip, Address, "GPU", LightingDeviceType.Gpu);
        chip.Log.Clear();

        var result = controller.SetLeds([new RgbColor(1, 2, 3), new RgbColor(4, 5, 6), new RgbColor(7, 8, 9)]);

        Assert.True(result.IsSuccess, result.ErrorMessage);
        Assert.Equal(["blk 8160=010302", "blk 8163=040605", "blk 8166=070908", "wr 80A0=01"], chip.Log);
        Assert.Equal(new RgbColor(7, 8, 9), controller.Describe(0).Colors[2]);
    }

    [Fact]
    public void SetLeds_WhenBlockWritesAreRefused_FallsBackToBytes()
    {
        var chip = new FakeEneChip(Address, "AUMA0-E6K5-0107", StrixTable()) { RefuseBlocks = true };
        chip[0x8021] = 1;
        var controller = new EneSmBusController(chip, Address, "GPU", LightingDeviceType.Gpu);
        chip.Log.Clear();

        Assert.True(controller.SetLeds([new RgbColor(1, 2, 3), new RgbColor(4, 5, 6), new RgbColor(7, 8, 9)]).IsSuccess);

        Assert.Equal(0x01, chip[0x8160]);
        Assert.Equal(0x03, chip[0x8161]);
        Assert.Equal(0x02, chip[0x8162]);
        Assert.Equal(0x08, chip[0x8168]);
        Assert.Contains("wr 8160=01", chip.Log);
    }

    [Fact]
    public void SetMode_Direct_ThenSetLeds_WritesTheDirectRegisters()
    {
        var chip = StrixChip();
        var controller = new EneSmBusController(chip, Address, "GPU", LightingDeviceType.Gpu);
        var direct = controller.Describe(0).Modes[0];
        chip.Log.Clear();

        Assert.True(controller.SetMode(direct).IsSuccess);
        Assert.Equal(["wr 8020=01", "wr 80A0=01"], chip.Log);
        chip.Log.Clear();

        Assert.True(controller.SetLeds([new RgbColor(255, 0, 0), new RgbColor(255, 0, 0), new RgbColor(255, 0, 0)]).IsSuccess);
        Assert.Equal(["blk 8100=FF0000", "blk 8103=FF0000", "blk 8106=FF0000"], chip.Log);
        Assert.Equal(0, controller.Describe(0).ActiveModeIndex);
    }

    [Fact]
    public void SetMode_Rainbow_WritesModeSpeedDirection_AndLeavesDirect()
    {
        var chip = StrixChip();
        var controller = new EneSmBusController(chip, Address, "GPU", LightingDeviceType.Gpu);
        var rainbow = controller.Describe(0).Modes[6] with { Speed = 4, Direction = LightingDirection.Right };
        chip.Log.Clear();

        Assert.True(controller.SetMode(rainbow).IsSuccess);

        Assert.Equal(["wr 8021=05", "wr 8022=04", "wr 8023=01", "wr 80A0=01", "wr 8020=00", "wr 80A0=01"], chip.Log);
        var device = controller.Describe(0);
        Assert.Equal(6, device.ActiveModeIndex);
        Assert.Equal(LightingDirection.Right, device.ActiveMode!.Direction);
    }

    [Fact]
    public void SetMode_ChaseWithRandomColors_UsesTheSpectrumVariant()
    {
        var chip = StrixChip();
        var controller = new EneSmBusController(chip, Address, "GPU", LightingDeviceType.Gpu);
        var chase = controller.Describe(0).Modes[8] with { ColorMode = LightingColorMode.Random };
        chip.Log.Clear();

        Assert.True(controller.SetMode(chase).IsSuccess);

        Assert.Equal("wr 8021=0A", chip.Log[0]);
        Assert.Equal(LightingColorMode.Random, controller.Describe(0).ActiveMode!.ColorMode);
    }

    [Fact]
    public void SaveMode_WritesTheSaveValue_AfterApplying()
    {
        var chip = StrixChip();
        var controller = new EneSmBusController(chip, Address, "GPU", LightingDeviceType.Gpu);
        chip.Log.Clear();

        Assert.True(controller.SaveMode(controller.Describe(0).Modes[1]).IsSuccess);

        Assert.Equal("wr 8021=00", chip.Log[0]);
        Assert.Equal("wr 80A0=AA", chip.Log[^1]);
    }

    [Fact]
    public void SetZoneLeds_WritesOnlyThatZone()
    {
        var chip = StrixChip();
        var controller = new EneSmBusController(chip, Address, "GPU", LightingDeviceType.Gpu);
        chip.Log.Clear();

        Assert.True(controller.SetZoneLeds(1, [new RgbColor(0x0A, 0x0B, 0x0C)]).IsSuccess);

        Assert.Equal(["blk 8166=0A0C0B", "wr 80A0=01"], chip.Log);
        Assert.Equal(new RgbColor(0xAA, 0xBB, 0xCC), controller.Describe(0).Colors[0]);
        Assert.Equal(new RgbColor(0x0A, 0x0B, 0x0C), controller.Describe(0).Colors[2]);
    }

    [Fact]
    public void GpuDetector_MatchesThePciIds_AndBuildsAGpuController()
    {
        var chip = StrixChip();
        var provider = new FakeBusProvider(chip);
        var (entries, inventory) = new NativeLightingBackend(hid: null, provider).Detect();

        var device = Assert.Single(inventory.Devices);
        Assert.Equal("ASUS ROG STRIX GeForce RTX 4080 Gaming", device.Name);
        Assert.Equal("ENE SMBus", device.Controller);
        Assert.Equal(LightingDeviceType.Gpu, entries[0].Controller.Describe(0).Type);
        Assert.Contains(inventory.Notes, n => n.StartsWith("HID devices: no transport", StringComparison.Ordinal));
    }

    [Fact]
    public void GpuDetector_IgnoresABusWithOtherIds()
    {
        var chip = new FakeEneChip(Address, "AUMA0-E6K5-0107", StrixTable(), subDevice: 0x1234);
        var (entries, inventory) = new NativeLightingBackend(hid: null, new FakeBusProvider(chip)).Detect();

        Assert.Empty(entries);
        Assert.Empty(inventory.Devices);
        Assert.Empty(chip.Log);
    }

    [Fact]
    public void GpuTable_ContainsTheKnownStrix4080Ids()
    {
        var strix = EneGpuDetectors.I2cPci.Where(d => d.Name == "ASUS ROG STRIX GeForce RTX 4080 Gaming").ToList();
        Assert.Contains(strix, d => d.PciVendor == 0x10DE && d.PciDevice == 0x2704 && d.PciSubsystemVendor == 0x1043 && d.PciSubsystemDevice == 0x88C0 && d.Address == 0x67);
        Assert.True(EneGpuDetectors.I2cPci.Count > 150);
        Assert.All(EneGpuDetectors.I2cPci, d => Assert.Equal(0x67, d.Address));
    }

    private sealed class FakeBusProvider(params II2cBus[] buses) : II2cBusProvider
    {
        public IReadOnlyList<II2cBus> Enumerate(List<string> notes)
        {
            notes.Add("fake bus provider");
            return buses;
        }
    }
}
