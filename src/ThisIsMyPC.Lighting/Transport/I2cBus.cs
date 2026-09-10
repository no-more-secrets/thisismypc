namespace ThisIsMyPC.Lighting.Transport;

/// <summary>
/// Identity of one I2C or SMBus master, keyed the way OpenRGB keys its PCI
/// detectors: the host device's PCI ids. A GPU's I2C port carries the GPU's
/// ids; a chipset SMBus carries the chipset's.
/// </summary>
public sealed record I2cBusInfo(
    string Name,
    ushort PciVendor,
    ushort PciDevice,
    ushort PciSubsystemVendor,
    ushort PciSubsystemDevice,
    int PortId);

/// <summary>
/// The SMBus transactions the ported controllers use, with i2c_smbus_interface's
/// conventions: reads return the value or -1, writes return 0 or -1, nothing
/// throws. Implementations serialize their own transactions.
/// </summary>
public interface II2cBus
{
    I2cBusInfo Info { get; }

    /// <summary>Largest block one <see cref="WriteBlockData"/> may carry.</summary>
    int MaxBlock { get; }

    /// <summary>SMBus receive byte: read one byte with no command.</summary>
    int ReadByte(byte address);

    int ReadByteData(byte address, byte command);

    int WriteByteData(byte address, byte command, byte value);

    int WriteWordData(byte address, byte command, ushort value);

    int WriteBlockData(byte address, byte command, ReadOnlySpan<byte> data);
}

/// <summary>Finds the buses this process can reach. Each provider notes what it could not open.</summary>
public interface II2cBusProvider
{
    /// <summary>Opens (or re-opens) every bus. Failures land in <see cref="Notes"/>, never in an exception.</summary>
    IReadOnlyList<II2cBus> Enumerate(List<string> notes);
}
