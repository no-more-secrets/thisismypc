using System.Buffers.Binary;
using System.Text;

namespace ThisIsMyPC.Core.Hardware;

/// <summary>Bounded SMBIOS structure reader, DMTF DSP0134 3.8.0 types 0, 1, 2, 3 and 17.</summary>
public static class SmbiosInventoryParser
{
    public static FirmwareInventory Parse(ReadOnlySpan<byte> table)
    {
        if (table.Length > 16 * 1024 * 1024) throw new InvalidDataException("Firmware table exceeds the size limit.");
        var result = new FirmwareInventory();
        var chassis = new List<int>();
        var memory = new List<MemoryDeviceIdentity>();
        while (!table.IsEmpty)
        {
            if (table.Length < 4 || table[1] < 4 || table[1] > table.Length)
                throw new InvalidDataException("Firmware structure is truncated.");
            var length = table[1];
            var end = length;
            while (end + 1 < table.Length && (table[end] != 0 || table[end + 1] != 0)) end++;
            if (end + 1 >= table.Length) throw new InvalidDataException("Firmware strings are unterminated.");
            var data = table[..length];
            var strings = Encoding.UTF8.GetString(table[length..end]).Split('\0');
            switch (data[0])
            {
                case 0:
                    result = result with { BiosVendor = Text(data, 4, strings), BiosVersion = Text(data, 5, strings), BiosDate = Text(data, 8, strings) };
                    break;
                case 1:
                    result = result with { Manufacturer = Text(data, 4, strings), Model = Text(data, 5, strings) };
                    break;
                case 2 when length > 13 && data[13] == 10: // Motherboard, not a daughterboard or management module.
                    result = result with { BoardManufacturer = Text(data, 4, strings), BoardProduct = Text(data, 5, strings), BoardVersion = Text(data, 6, strings) };
                    break;
                case 3 when length > 5:
                    chassis.Add(data[5] & 0x7f); // High bit is the chassis lock, not part of its type.
                    break;
                case 17 when length >= 21:
                    var size = U16(data, 12);
                    ulong? bytes = size switch
                    {
                        0xffff => null,
                        0x7fff => length >= 32 ? (U32(data, 28) & 0x7fffffffUL) * 1024 * 1024 : null,
                        _ => (ulong)(size & 0x7fff) * ((size & 0x8000) != 0 ? 1024UL : 1024UL * 1024),
                    };
                    uint? speed = length >= 34 ? U16(data, 32) : null;
                    if (speed == 0xffff) speed = length >= 92 ? U32(data, 88) : null;
                    if (speed == 0) speed = null;
                    var type = data[18] switch { 0x12 => "DDR", 0x13 => "DDR2", 0x18 => "DDR3", 0x1a => "DDR4", 0x22 => "DDR5", 0x1e => "LPDDR4", 0x23 => "LPDDR5", _ => null };
                    memory.Add(new(Text(data, 16, strings), bytes, type, speed));
                    break;
            }
            if (data[0] == 127) break;
            table = table[(end + 2)..];
        }
        return result with { ChassisTypes = chassis.AsReadOnly(), MemoryDevices = memory.AsReadOnly() };
    }

    private static string? Text(ReadOnlySpan<byte> data, int offset, string[] strings)
    {
        if (offset >= data.Length || data[offset] == 0 || data[offset] > strings.Length) return null;
        // The shared normalizer also rejects firmware placeholders.
        return MachineIdentity.From(strings[data[offset] - 1], null).Manufacturer;
    }

    private static ushort U16(ReadOnlySpan<byte> data, int offset) => BinaryPrimitives.ReadUInt16LittleEndian(data[offset..]);
    private static uint U32(ReadOnlySpan<byte> data, int offset) => BinaryPrimitives.ReadUInt32LittleEndian(data[offset..]);
}
