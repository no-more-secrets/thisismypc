using ThisIsMyPC.Lighting.Transport;

namespace ThisIsMyPC.Lighting.Tests.Fakes;

/// <summary>
/// An SMBus bus with one simulated ENE controller on it: the 16-bit register
/// space, the address-then-data protocol (word at 0x00 selects a register,
/// 0x81 reads it, 0x01 writes it, 0x03 writes a block), the counting
/// registers 0xA0 to 0xAF the probe checks, and the auto-increment the real
/// chip applies after each data byte.
/// </summary>
internal sealed class FakeEneChip : II2cBus
{
    private readonly byte[] _registers = new byte[0x10000];
    private ushort _pointer;

    public FakeEneChip(byte address, string version, byte[] configTable, string busName = "Fake I2C", ushort vendor = 0x10DE, ushort device = 0x2704, ushort subVendor = 0x1043, ushort subDevice = 0x88C0)
    {
        Address = address;
        Info = new I2cBusInfo(busName, vendor, device, subVendor, subDevice, 1);
        var name = System.Text.Encoding.ASCII.GetBytes(version);
        Array.Copy(name, 0, _registers, 0x1000, Math.Min(16, name.Length));
        Array.Copy(configTable, 0, _registers, 0x1C00, Math.Min(64, configTable.Length));
    }

    public byte Address { get; }
    public I2cBusInfo Info { get; }
    public int MaxBlock { get; init; } = 3;

    /// <summary>Every transaction as "op addr cmd value" for assertions.</summary>
    public List<string> Log { get; } = [];

    /// <summary>Block writes fail with -1 so the byte-wise fallback runs.</summary>
    public bool RefuseBlocks { get; init; }

    public byte this[ushort register]
    {
        get => _registers[register];
        set => _registers[register] = value;
    }

    public int ReadByte(byte address)
    {
        Log.Add($"rb {address:X2}");
        return address == Address ? 0 : -1;
    }

    public int ReadByteData(byte address, byte command)
    {
        if (address != Address)
            return -1;
        if (command is >= 0xA0 and < 0xB0)
        {
            Log.Add($"rbd {address:X2} {command:X2}");
            return command - 0xA0;
        }
        if (command == 0x81)
        {
            var value = _registers[_pointer];
            Log.Add($"rd {_pointer:X4}={value:X2}");
            _pointer++;
            return value;
        }
        if (command == 0x00)
            return 0;
        return -1;
    }

    public int WriteByteData(byte address, byte command, byte value)
    {
        if (address != Address || command != 0x01)
            return -1;
        Log.Add($"wr {_pointer:X4}={value:X2}");
        _registers[_pointer] = value;
        _pointer++;
        return 0;
    }

    public int WriteWordData(byte address, byte command, ushort value)
    {
        if (address != Address || command != 0x00)
            return -1;
        _pointer = (ushort)(((value & 0xFF) << 8) | (value >> 8));
        return 0;
    }

    public int WriteBlockData(byte address, byte command, ReadOnlySpan<byte> data)
    {
        if (address != Address || command != 0x03 || RefuseBlocks || data.Length > MaxBlock)
            return -1;
        Log.Add($"blk {_pointer:X4}={Convert.ToHexString(data)}");
        foreach (var value in data)
            _registers[_pointer++] = value;
        return 0;
    }
}
