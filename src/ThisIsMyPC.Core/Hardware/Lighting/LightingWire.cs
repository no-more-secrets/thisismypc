using System.Buffers.Binary;
using System.Text;
using ThisIsMyPC.Core.Hardware.Detection;

namespace ThisIsMyPC.Core.Hardware.Lighting;

/// <summary>
/// The controller description and the update payloads of the OpenRGB SDK,
/// pure. Field order and the protocol-version conditions follow
/// RGBController::GetDeviceDescriptionData, GetModeDescriptionData,
/// GetZoneDescriptionData, GetSegmentDescriptionData, GetLEDDescriptionData
/// and RGBController_Network::CreateUpdate*Packet in the OpenRGB repository.
/// Strings are a 16-bit length that includes the null terminator, then the
/// bytes. A malformed description throws <see cref="InvalidDataException"/>;
/// nothing is guessed.
/// </summary>
public static class LightingWire
{
    /// <summary>Parses a REQUEST_CONTROLLER_DATA reply payload: a 32-bit total size, then the description.</summary>
    public static LightingDevice ParseDevice(ReadOnlySpan<byte> payload, uint protocolVersion, int deviceIndex)
    {
        var reader = new Reader(payload);
        var declared = reader.UInt32();
        if (declared != payload.Length)
            throw new InvalidDataException($"Controller data declares {declared} bytes but {payload.Length} arrived.");

        var type = (int)reader.UInt32();
        var name = reader.String();
        var vendor = protocolVersion >= 1 ? reader.String() : string.Empty;
        var description = reader.String();
        var version = reader.String();
        var serial = reader.String();
        var location = reader.String();

        var modeCount = reader.UInt16();
        var activeMode = reader.Int32();
        var modes = new List<LightingMode>(modeCount);
        for (var i = 0; i < modeCount; i++)
            modes.Add(ReadMode(ref reader, protocolVersion, i));

        var zoneCount = reader.UInt16();
        var zones = new List<LightingZone>(zoneCount);
        for (var i = 0; i < zoneCount; i++)
            zones.Add(ReadZone(ref reader, protocolVersion, i));

        var ledCount = reader.UInt16();
        var leds = new List<LightingLed>(ledCount);
        for (var i = 0; i < ledCount; i++)
        {
            var ledName = reader.String();
            var value = protocolVersion < 6 ? reader.UInt32() : 0;
            leds.Add(new LightingLed(ledName, value));
        }

        var colorCount = reader.UInt16();
        var colors = new List<RgbColor>(colorCount);
        for (var i = 0; i < colorCount; i++)
            colors.Add(RgbColor.FromWire(reader.UInt32()));

        // Later versions append fields this client does not use; they are
        // read so a mismatch still surfaces as a size error, never a crash.
        if (protocolVersion >= 5)
        {
            var displayNames = reader.UInt16();
            for (var i = 0; i < displayNames; i++)
                reader.String();
            reader.UInt32(); // controller flags
        }
        if (protocolVersion >= 6)
        {
            reader.String(); // display name
            var configurationLength = reader.UInt32();
            reader.Skip(checked((int)configurationLength));
        }

        return new LightingDevice
        {
            Index = deviceIndex,
            Type = Enum.IsDefined(typeof(LightingDeviceType), type) ? (LightingDeviceType)type : LightingDeviceType.Unknown,
            Name = name,
            Vendor = vendor,
            Description = description,
            Version = version,
            Serial = serial,
            Location = location,
            ActiveModeIndex = activeMode,
            Modes = modes,
            Zones = zones,
            Leds = leds,
            Colors = colors,
        };
    }

    private static LightingMode ReadMode(ref Reader reader, uint protocolVersion, int index)
    {
        var name = reader.String();
        var value = protocolVersion < 6 ? reader.Int32() : 0;
        var flags = reader.UInt32();
        var speedMin = reader.UInt32();
        var speedMax = reader.UInt32();
        uint brightnessMin = 0, brightnessMax = 0;
        if (protocolVersion >= 3)
        {
            brightnessMin = reader.UInt32();
            brightnessMax = reader.UInt32();
        }
        var colorsMin = reader.UInt32();
        var colorsMax = reader.UInt32();
        var speed = reader.UInt32();
        uint brightness = 0;
        if (protocolVersion >= 3)
            brightness = reader.UInt32();
        var direction = reader.UInt32();
        var colorMode = reader.UInt32();
        var colorCount = reader.UInt16();
        var colors = new List<RgbColor>(colorCount);
        for (var i = 0; i < colorCount; i++)
            colors.Add(RgbColor.FromWire(reader.UInt32()));

        return new LightingMode
        {
            Index = index,
            Name = name,
            Value = value,
            Flags = (LightingModeFlags)flags,
            SpeedMin = speedMin,
            SpeedMax = speedMax,
            BrightnessMin = brightnessMin,
            BrightnessMax = brightnessMax,
            ColorsMin = colorsMin,
            ColorsMax = colorsMax,
            Speed = speed,
            Brightness = brightness,
            Direction = (LightingDirection)direction,
            ColorMode = (LightingColorMode)colorMode,
            Colors = colors,
        };
    }

    private static LightingZone ReadZone(ref Reader reader, uint protocolVersion, int index)
    {
        var name = reader.String();
        var type = (int)reader.UInt32();
        var ledsMin = reader.UInt32();
        var ledsMax = reader.UInt32();
        var ledCount = reader.UInt32();

        var matrixLength = reader.UInt16();
        uint height = 0, width = 0;
        if (matrixLength > 0)
        {
            if (matrixLength < 8)
                throw new InvalidDataException("Zone matrix map is shorter than its header.");
            height = reader.UInt32();
            width = reader.UInt32();
            reader.Skip(matrixLength - 8);
        }

        var segments = new List<LightingSegment>();
        if (protocolVersion >= 4)
        {
            var segmentCount = reader.UInt16();
            for (var i = 0; i < segmentCount; i++)
            {
                var segmentName = reader.String();
                var segmentType = (int)reader.UInt32();
                var start = reader.UInt32();
                var count = reader.UInt32();
                if (protocolVersion >= 6)
                {
                    var segmentMatrix = reader.UInt16();
                    reader.Skip(segmentMatrix);
                    reader.UInt32(); // segment flags
                }
                segments.Add(new LightingSegment(segmentName, ToZoneType(segmentType), start, count));
            }
        }

        uint flags = 0;
        if (protocolVersion >= 5)
            flags = reader.UInt32();
        if (protocolVersion >= 6)
        {
            reader.Int32(); // zone active mode
            var zoneModes = reader.UInt16();
            for (var i = 0; i < zoneModes; i++)
                ReadMode(ref reader, protocolVersion, i);
            reader.String(); // zone display name
        }

        return new LightingZone
        {
            Index = index,
            Name = name,
            Type = ToZoneType(type),
            LedsMin = ledsMin,
            LedsMax = ledsMax,
            LedCount = ledCount,
            MatrixHeight = height,
            MatrixWidth = width,
            Segments = segments,
            Flags = flags,
        };
    }

    private static LightingZoneType ToZoneType(int value) =>
        Enum.IsDefined(typeof(LightingZoneType), value) ? (LightingZoneType)value : LightingZoneType.Linear;

    // ---- update payloads ----

    /// <summary>RGBCONTROLLER_UPDATELEDS: 32-bit size, 16-bit count, then every LED color.</summary>
    public static byte[] BuildUpdateLeds(uint deviceIndex, IReadOnlyList<RgbColor> colors)
    {
        ArgumentNullException.ThrowIfNull(colors);
        var payload = new byte[4 + 2 + 4 * colors.Count];
        BinaryPrimitives.WriteUInt32LittleEndian(payload, (uint)payload.Length);
        BinaryPrimitives.WriteUInt16LittleEndian(payload.AsSpan(4), (ushort)colors.Count);
        for (var i = 0; i < colors.Count; i++)
            BinaryPrimitives.WriteUInt32LittleEndian(payload.AsSpan(6 + 4 * i), colors[i].ToWire());
        return OpenRgbSdkProtocol.BuildPacket(deviceIndex, OpenRgbSdkProtocol.UpdateLeds, payload);
    }

    /// <summary>RGBCONTROLLER_UPDATEZONELEDS: 32-bit size, 32-bit zone index, 16-bit count, colors.</summary>
    public static byte[] BuildUpdateZoneLeds(uint deviceIndex, int zoneIndex, IReadOnlyList<RgbColor> colors)
    {
        ArgumentNullException.ThrowIfNull(colors);
        var payload = new byte[4 + 4 + 2 + 4 * colors.Count];
        BinaryPrimitives.WriteUInt32LittleEndian(payload, (uint)payload.Length);
        BinaryPrimitives.WriteInt32LittleEndian(payload.AsSpan(4), zoneIndex);
        BinaryPrimitives.WriteUInt16LittleEndian(payload.AsSpan(8), (ushort)colors.Count);
        for (var i = 0; i < colors.Count; i++)
            BinaryPrimitives.WriteUInt32LittleEndian(payload.AsSpan(10 + 4 * i), colors[i].ToWire());
        return OpenRgbSdkProtocol.BuildPacket(deviceIndex, OpenRgbSdkProtocol.UpdateZoneLeds, payload);
    }

    /// <summary>RGBCONTROLLER_UPDATESINGLELED: 32-bit LED index, then the color. No size prefix.</summary>
    public static byte[] BuildUpdateSingleLed(uint deviceIndex, int ledIndex, RgbColor color)
    {
        var payload = new byte[8];
        BinaryPrimitives.WriteInt32LittleEndian(payload, ledIndex);
        BinaryPrimitives.WriteUInt32LittleEndian(payload.AsSpan(4), color.ToWire());
        return OpenRgbSdkProtocol.BuildPacket(deviceIndex, OpenRgbSdkProtocol.UpdateSingleLed, payload);
    }

    /// <summary>RGBCONTROLLER_SETCUSTOMMODE: no payload; the device switches to its per-LED mode.</summary>
    public static byte[] BuildSetCustomMode(uint deviceIndex) =>
        OpenRgbSdkProtocol.BuildPacket(deviceIndex, OpenRgbSdkProtocol.SetCustomMode, []);

    /// <summary>RGBCONTROLLER_UPDATEMODE (or SAVEMODE): 32-bit size, 32-bit mode index, then the mode description.</summary>
    public static byte[] BuildUpdateMode(uint deviceIndex, LightingMode mode, uint protocolVersion, bool save = false)
    {
        ArgumentNullException.ThrowIfNull(mode);
        var description = WriteMode(mode, protocolVersion);
        var payload = new byte[4 + 4 + description.Length];
        BinaryPrimitives.WriteUInt32LittleEndian(payload, (uint)payload.Length);
        BinaryPrimitives.WriteInt32LittleEndian(payload.AsSpan(4), mode.Index);
        description.CopyTo(payload, 8);
        return OpenRgbSdkProtocol.BuildPacket(deviceIndex, save ? OpenRgbSdkProtocol.SaveMode : OpenRgbSdkProtocol.UpdateMode, payload);
    }

    /// <summary>GetModeDescriptionData, mirrored so the server reads back what it sent.</summary>
    public static byte[] WriteMode(LightingMode mode, uint protocolVersion)
    {
        ArgumentNullException.ThrowIfNull(mode);
        var writer = new Writer();
        writer.String(mode.Name);
        if (protocolVersion < 6)
            writer.Int32(mode.Value);
        writer.UInt32((uint)mode.Flags);
        writer.UInt32(mode.SpeedMin);
        writer.UInt32(mode.SpeedMax);
        if (protocolVersion >= 3)
        {
            writer.UInt32(mode.BrightnessMin);
            writer.UInt32(mode.BrightnessMax);
        }
        writer.UInt32(mode.ColorsMin);
        writer.UInt32(mode.ColorsMax);
        writer.UInt32(mode.Speed);
        if (protocolVersion >= 3)
            writer.UInt32(mode.Brightness);
        writer.UInt32((uint)mode.Direction);
        writer.UInt32((uint)mode.ColorMode);
        writer.UInt16((ushort)mode.Colors.Count);
        foreach (var color in mode.Colors)
            writer.UInt32(color.ToWire());
        return writer.ToArray();
    }

    private ref struct Reader(ReadOnlySpan<byte> data)
    {
        private readonly ReadOnlySpan<byte> _data = data;
        private int _offset;

        public uint UInt32()
        {
            Need(4);
            var value = BinaryPrimitives.ReadUInt32LittleEndian(_data.Slice(_offset, 4));
            _offset += 4;
            return value;
        }

        public int Int32() => unchecked((int)UInt32());

        public ushort UInt16()
        {
            Need(2);
            var value = BinaryPrimitives.ReadUInt16LittleEndian(_data.Slice(_offset, 2));
            _offset += 2;
            return value;
        }

        public string String()
        {
            var length = UInt16();
            if (length == 0)
                return string.Empty;
            Need(length);
            var bytes = _data.Slice(_offset, length);
            _offset += length;
            var terminator = bytes.IndexOf((byte)0);
            return Encoding.UTF8.GetString(terminator >= 0 ? bytes[..terminator] : bytes);
        }

        public void Skip(int count)
        {
            Need(count);
            _offset += count;
        }

        private readonly void Need(int count)
        {
            if (count < 0 || _offset + count > _data.Length)
                throw new InvalidDataException($"Controller data ends at byte {_data.Length}; {count} more bytes were expected at {_offset}.");
        }
    }

    private sealed class Writer
    {
        private readonly List<byte> _bytes = [];

        public void UInt32(uint value)
        {
            Span<byte> span = stackalloc byte[4];
            BinaryPrimitives.WriteUInt32LittleEndian(span, value);
            _bytes.AddRange(span);
        }

        public void Int32(int value) => UInt32(unchecked((uint)value));

        public void UInt16(ushort value)
        {
            Span<byte> span = stackalloc byte[2];
            BinaryPrimitives.WriteUInt16LittleEndian(span, value);
            _bytes.AddRange(span);
        }

        public void String(string value)
        {
            var bytes = Encoding.UTF8.GetBytes(value);
            UInt16((ushort)(bytes.Length + 1));
            _bytes.AddRange(bytes);
            _bytes.Add(0);
        }

        public byte[] ToArray() => [.. _bytes];
    }
}
