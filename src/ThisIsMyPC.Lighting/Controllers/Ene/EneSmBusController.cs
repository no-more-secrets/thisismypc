using System.Text;
using ThisIsMyPC.Core.Hardware.Lighting;
using ThisIsMyPC.Core.Results;
using ThisIsMyPC.Lighting.Transport;

namespace ThisIsMyPC.Lighting.Controllers.Ene;

/// <summary>
/// ENE (formerly ASUS Aura) SMBus lighting controllers: ASUS graphics cards
/// and motherboards, ENE-based RGB memory. Port of OpenRGB's
/// ENESMBusController, ENESMBusInterface_i2c_smbus and RGBController_ENESMBus.
/// The chip has a 16-bit register space reached through SMBus: write the
/// register address as a byte-swapped word at command 0x00, then read at
/// 0x81 or write at 0x01 (0x03 for a block). Colors are stored R, B, G.
/// </summary>
public sealed class EneSmBusController : ILightingController
{
    private const byte ApplyValue = 0x01;
    private const byte SaveValue = 0xAA;
    private const int ZoneSlots = 8;

    private const ushort RegDeviceName = 0x1000;
    internal const ushort RegMicronCheck = 0x1030;
    private const ushort RegConfigTable = 0x1C00;
    private const ushort RegColorsDirect = 0x8000;
    private const ushort RegColorsEffect = 0x8010;
    private const ushort RegDirect = 0x8020;
    private const ushort RegMode = 0x8021;
    private const ushort RegSpeed = 0x8022;
    private const ushort RegDirection = 0x8023;
    private const ushort RegApply = 0x80A0;
    private const ushort RegColorsDirectV2 = 0x8100;
    private const ushort RegColorsEffectV2 = 0x8160;

    internal const int ModeDirect = 0xFFFF;
    internal const int ModeOff = 0;
    internal const int ModeStatic = 1;
    internal const int ModeBreathing = 2;
    internal const int ModeFlashing = 3;
    internal const int ModeSpectrumCycle = 4;
    internal const int ModeRainbow = 5;
    internal const int ModeSpectrumCycleBreathing = 6;
    internal const int ModeChaseFade = 7;
    internal const int ModeSpectrumCycleChaseFade = 8;
    internal const int ModeChase = 9;
    internal const int ModeSpectrumCycleChase = 10;
    internal const int ModeRandomFlicker = 13;
    internal const int ModeDoubleFade = 14;

    /// <summary>Speeds count down: 4 is slowest, 0 fastest. The mode reports min 4, max 0 and the page keeps that order.</summary>
    private const uint SpeedSlowest = 0x04;
    private const uint SpeedNormal = 0x02;
    private const uint SpeedFastest = 0x00;

    private const byte ConfigChannelV1 = 0x13;
    private const byte ConfigChannelV2 = 0x1B;

    private readonly II2cBus _bus;
    private readonly byte _address;
    private readonly string _name;
    private readonly LightingDeviceType _type;
    private readonly byte[] _configTable = new byte[64];
    private readonly string _version;
    private readonly ushort _directRegister;
    private readonly ushort _effectRegister;
    private readonly byte _channelConfig;
    private readonly bool _supportsDoubleFade;
    private readonly List<LightingMode> _modes;
    private readonly List<LightingZone> _zones;
    private readonly List<LightingLed> _leds;
    private readonly RgbColor[] _colors;
    private int _activeMode;

    public EneSmBusController(II2cBus bus, byte address, string name, LightingDeviceType type)
    {
        ArgumentNullException.ThrowIfNull(bus);
        _bus = bus;
        _address = address;
        _name = name;
        _type = type;

        _version = ReadDeviceName();
        for (var i = 0; i < _configTable.Length; i++)
            _configTable[i] = (byte)Math.Max(0, RegisterRead((ushort)(RegConfigTable + i)));

        // ENESMBusController's constructor: register layout by firmware name.
        // The LED total OpenRGB reads from the table (0x02, or 0x03 on the
        // 0107 family) is the same number the zone slots at 0x03 add up to on
        // every device seen; the zone sum sizes the color table here.
        (_directRegister, _effectRegister, _channelConfig) = _version switch
        {
            "LED-0116" or "DIMM_LED-0102" or "AUMA0-E8K4-0101" => (RegColorsDirect, RegColorsEffect, ConfigChannelV1),
            "AUDA0-E6K5-0101" => (RegColorsDirectV2, RegColorsEffectV2, ConfigChannelV1),
            "AUMA0-E6K5-0106" or "AUMA0-E6K5-0105" or "AUMA0-E6K5-0104" => (RegColorsDirectV2, RegColorsEffectV2, ConfigChannelV2),
            "AUMA0-E6K5-0107" or "AUMA0-E6K5-1110" or "AUMA0-E6K5-1111" or "AUMA0-E6K5-1107"
                or "AUMA0-E6K5-1113" or "AUMA0-E6K5-1114" => (RegColorsDirectV2, RegColorsEffectV2, ConfigChannelV2),
            "AUMA0-E6K5-0008" => (RegColorsDirectV2, RegColorsEffectV2, ConfigChannelV1),
            _ => (RegColorsDirect, RegColorsEffect, ConfigChannelV1),
        };
        if (_version == "AUDA0-E6K5-0101")
        {
            for (var zone = 0; zone < ZoneSlots; zone++)
                _supportsDoubleFade |= _configTable[_channelConfig + zone] == (byte)EneChannel.Dram3;
        }

        (_zones, _leds) = BuildZones();
        _colors = new RgbColor[_leds.Count];
        _modes = BuildModes();
        _activeMode = ReadDeviceMode();
    }

    public string Family => "ENE SMBus";

    public string Version => _version;

    private enum EneChannel : byte
    {
        Dram2 = 0x05,
        Dram3 = 0x0E,
        CenterStart = 0x82,
        Center = 0x83,
        Audio = 0x84,
        BackIo = 0x85,
        RgbHeader = 0x86,
        RgbHeader2 = 0x87,
        Backplate = 0x88,
        Dram = 0x8A,
        Pcie = 0x8B,
        RgbHeader3 = 0x91,
    }

    private string ChannelName(int zone) => (EneChannel)_configTable[_channelConfig + zone] switch
    {
        EneChannel.Audio => "Audio",
        EneChannel.Backplate => "Backplate",
        EneChannel.BackIo => "Back I/O",
        EneChannel.Center or EneChannel.CenterStart => "Center",
        EneChannel.Dram or EneChannel.Dram2 or EneChannel.Dram3 => "DRAM",
        EneChannel.Pcie => "PCIe",
        EneChannel.RgbHeader or EneChannel.RgbHeader3 => "RGB Header",
        EneChannel.RgbHeader2 => "RGB Header 2",
        _ => "Unknown",
    };

    /// <summary>RGBController_ENESMBus::SetupZones: one zone per named channel, LED counts from the table at 0x03 + zone.</summary>
    private (List<LightingZone>, List<LightingLed>) BuildZones()
    {
        var zones = new List<(string Name, uint Count)>();
        for (var slot = 0; slot < ZoneSlots; slot++)
        {
            uint count = _configTable[0x03 + slot];
            if (count == 0)
                continue;
            var name = ChannelName(slot);
            var existing = zones.FindIndex(z => z.Name == name);
            if (existing < 0)
                zones.Add((name, count));
            else
                zones[existing] = (name, zones[existing].Count + count);
        }

        var result = new List<LightingZone>();
        var leds = new List<LightingLed>();
        for (var z = 0; z < zones.Count; z++)
        {
            var (name, count) = zones[z];
            result.Add(new LightingZone
            {
                Index = z,
                Name = name,
                Type = count > 1 ? LightingZoneType.Linear : LightingZoneType.Single,
                LedsMin = count,
                LedsMax = count,
                LedCount = count,
            });
            for (var i = 0; i < count; i++)
                leds.Add(new LightingLed($"{name} LED {i + 1}", (uint)leds.Count));
        }
        return (result, leds);
    }

    private List<LightingMode> BuildModes()
    {
        const LightingModeFlags save = LightingModeFlags.ManualSave;
        const LightingModeFlags perLed = LightingModeFlags.HasPerLedColor;
        const LightingModeFlags speed = LightingModeFlags.HasSpeed;
        const LightingModeFlags leftRight = LightingModeFlags.HasDirectionLeftRight;
        const LightingModeFlags random = LightingModeFlags.HasRandomColor;
        var modes = new List<LightingMode>
        {
            new() { Index = 0, Name = "Direct", Value = ModeDirect, Flags = perLed, ColorMode = LightingColorMode.PerLed },
            new() { Index = 1, Name = "Off", Value = ModeOff, Flags = save, ColorMode = LightingColorMode.None },
            new() { Index = 2, Name = "Static", Value = ModeStatic, Flags = perLed | save, ColorMode = LightingColorMode.PerLed },
            Speedy(3, "Breathing", ModeBreathing, random | perLed | speed | save, LightingColorMode.PerLed),
            Speedy(4, "Flashing", ModeFlashing, perLed | speed | save, LightingColorMode.PerLed),
            Speedy(5, "Spectrum Cycle", ModeSpectrumCycle, speed | save, LightingColorMode.None),
            Speedy(6, "Rainbow", ModeRainbow, speed | leftRight | save, LightingColorMode.None),
            Speedy(7, "Chase Fade", ModeChaseFade, random | perLed | speed | leftRight | save, LightingColorMode.PerLed),
            Speedy(8, "Chase", ModeChase, random | perLed | speed | leftRight | save, LightingColorMode.PerLed),
            Speedy(9, "Random Flicker", ModeRandomFlicker, speed | save, LightingColorMode.None),
        };
        if (_supportsDoubleFade)
            modes.Add(Speedy(10, "Double Fade", ModeDoubleFade, speed, LightingColorMode.None));
        return modes;
    }

    private static LightingMode Speedy(int index, string name, int value, LightingModeFlags flags, LightingColorMode colorMode) => new()
    {
        Index = index,
        Name = name,
        Value = value,
        Flags = flags,
        SpeedMin = SpeedSlowest,
        SpeedMax = SpeedFastest,
        Speed = SpeedNormal,
        Direction = LightingDirection.Left,
        ColorMode = colorMode,
    };

    /// <summary>RGBController_ENESMBus::GetDeviceMode: the mode, speed, direction and colors the chip runs now.</summary>
    private int ReadDeviceMode()
    {
        var deviceMode = RegisterRead(RegMode);
        var speed = RegisterRead(RegSpeed);
        var direction = RegisterRead(RegDirection);
        if (RegisterRead(RegDirect) > 0)
            deviceMode = ModeDirect;

        var colorMode = LightingColorMode.PerLed;
        switch (deviceMode)
        {
            case ModeOff or ModeRainbow or ModeSpectrumCycle or ModeRandomFlicker or ModeDoubleFade:
                colorMode = LightingColorMode.None;
                break;
            case ModeSpectrumCycleChase:
                deviceMode = ModeChase;
                colorMode = LightingColorMode.Random;
                break;
            case ModeSpectrumCycleBreathing:
                deviceMode = ModeBreathing;
                colorMode = LightingColorMode.Random;
                break;
            case ModeSpectrumCycleChaseFade:
                deviceMode = ModeChaseFade;
                colorMode = LightingColorMode.Random;
                break;
        }

        var active = 0;
        for (var i = 0; i < _modes.Count; i++)
        {
            var mode = _modes[i];
            if (mode.Value != deviceMode)
                continue;
            active = i;
            _modes[i] = mode with
            {
                ColorMode = colorMode,
                Speed = mode.HasSpeed && speed >= 0 ? (uint)speed : mode.Speed,
                Direction = mode.HasDirection && direction >= 0 ? (direction == 0 ? LightingDirection.Left : LightingDirection.Right) : mode.Direction,
            };
            break;
        }

        var colorRegister = active == 0 ? _directRegister : _effectRegister;
        for (var led = 0; led < _colors.Length; led++)
        {
            var red = RegisterRead((ushort)(colorRegister + (3 * led)));
            var blue = RegisterRead((ushort)(colorRegister + (3 * led) + 1));
            var green = RegisterRead((ushort)(colorRegister + (3 * led) + 2));
            _colors[led] = new RgbColor((byte)Math.Max(0, red), (byte)Math.Max(0, green), (byte)Math.Max(0, blue));
        }
        return active;
    }

    public LightingDevice Describe(int index) => new()
    {
        Index = index,
        Type = _type,
        Name = _name,
        Vendor = _version.Contains("DIMM_LED", StringComparison.Ordinal) || _version.Contains("AUDA", StringComparison.Ordinal) ? "ENE" : "ASUS",
        Description = "ENE SMBus device",
        Version = _version,
        Location = $"I2C: {_bus.Info.Name}, address 0x{_address:X2}",
        ActiveModeIndex = _activeMode,
        Modes = _modes.ToList(),
        Zones = _zones,
        Leds = _leds,
        Colors = _colors.ToList(),
    };

    public OperationResult<bool> SetMode(LightingMode mode)
    {
        ArgumentNullException.ThrowIfNull(mode);
        if (mode.Index < 0 || mode.Index >= _modes.Count)
            return OperationResult<bool>.Failure("The device has no such mode.", ErrorCategory.NotFound);
        var stored = _modes[mode.Index];
        var colorMode = mode.ColorMode == LightingColorMode.Random && stored.HasRandomColor ? LightingColorMode.Random : StoredColorMode(stored);
        _modes[mode.Index] = stored with
        {
            Speed = stored.HasSpeed ? mode.Speed : stored.Speed,
            Direction = stored.HasDirection ? mode.Direction : stored.Direction,
            ColorMode = colorMode,
        };
        _activeMode = mode.Index;
        return Apply(save: false);
    }

    private static LightingColorMode StoredColorMode(LightingMode mode) =>
        mode.HasPerLedColor ? LightingColorMode.PerLed : LightingColorMode.None;

    /// <summary>RGBController_ENESMBus::DeviceUpdateMode, plus the save register when asked.</summary>
    private OperationResult<bool> Apply(bool save)
    {
        var mode = _modes[_activeMode];
        if (mode.Value == ModeDirect)
        {
            if (!SetDirect(true))
                return Failure();
        }
        else
        {
            var value = mode.Value;
            if (mode.ColorMode == LightingColorMode.Random)
            {
                value = value switch
                {
                    ModeChase => ModeSpectrumCycleChase,
                    ModeBreathing => ModeSpectrumCycleBreathing,
                    ModeChaseFade => ModeSpectrumCycleChaseFade,
                    _ => value,
                };
            }
            var speed = mode.HasSpeed ? (byte)mode.Speed : (byte)0;
            var direction = mode.HasDirection && mode.Direction == LightingDirection.Right ? (byte)1 : (byte)0;
            if (RegisterWrite(RegMode, (byte)value) < 0
                || RegisterWrite(RegSpeed, speed) < 0
                || RegisterWrite(RegDirection, direction) < 0
                || RegisterWrite(RegApply, ApplyValue) < 0
                || !SetDirect(false))
            {
                return Failure();
            }
        }
        if (save && RegisterWrite(RegApply, SaveValue) < 0)
            return Failure();
        return OperationResult<bool>.Success(true);
    }

    public OperationResult<bool> SetLeds(IReadOnlyList<RgbColor> colors)
    {
        ArgumentNullException.ThrowIfNull(colors);
        for (var i = 0; i < _colors.Length && i < colors.Count; i++)
            _colors[i] = colors[i];
        return WriteAllColors();
    }

    public OperationResult<bool> SetZoneLeds(int zoneIndex, IReadOnlyList<RgbColor> colors)
    {
        ArgumentNullException.ThrowIfNull(colors);
        if (zoneIndex < 0 || zoneIndex >= _zones.Count)
            return OperationResult<bool>.Failure("The device has no such zone.", ErrorCategory.NotFound);
        var start = 0;
        for (var z = 0; z < zoneIndex; z++)
            start += (int)_zones[z].LedCount;
        var count = (int)_zones[zoneIndex].LedCount;
        for (var i = 0; i < count && i < colors.Count && start + i < _colors.Length; i++)
            _colors[start + i] = colors[i];

        // DeviceUpdateZoneLEDs writes each LED on its own. OpenRGB applies
        // after every LED in an effect mode; one apply after the zone latches
        // the whole table at once instead of stepping through partial states.
        var register = _activeMode == 0 ? _directRegister : _effectRegister;
        for (var i = 0; i < count && start + i < _colors.Length; i++)
        {
            var color = _colors[start + i];
            if (RegisterWriteBlock((ushort)(register + (3 * (start + i))), [color.R, color.B, color.G]) < 0)
                return Failure();
        }
        if (_activeMode != 0 && RegisterWrite(RegApply, ApplyValue) < 0)
            return Failure();
        return OperationResult<bool>.Success(true);
    }

    public OperationResult<bool> SaveMode(LightingMode mode)
    {
        var applied = SetMode(mode);
        return applied.IsSuccess ? Apply(save: true) : applied;
    }

    /// <summary>SetAllColorsDirect / SetAllColorsEffect: the whole color table in blocks of MaxBlock bytes.</summary>
    private OperationResult<bool> WriteAllColors()
    {
        var buffer = new byte[_colors.Length * 3];
        for (var i = 0; i < _colors.Length; i++)
        {
            buffer[(3 * i) + 0] = _colors[i].R;
            buffer[(3 * i) + 1] = _colors[i].B;
            buffer[(3 * i) + 2] = _colors[i].G;
        }
        var register = _activeMode == 0 ? _directRegister : _effectRegister;
        var sent = 0;
        var block = Math.Max(1, _bus.MaxBlock);
        while (sent < buffer.Length)
        {
            var size = Math.Min(block, buffer.Length - sent);
            if (RegisterWriteBlock((ushort)(register + sent), buffer.AsSpan(sent, size)) < 0)
                return Failure();
            sent += size;
        }
        if (_activeMode != 0 && RegisterWrite(RegApply, ApplyValue) < 0)
            return Failure();
        return OperationResult<bool>.Success(true);
    }

    private bool SetDirect(bool direct) =>
        RegisterWrite(RegDirect, direct ? (byte)1 : (byte)0) >= 0 && RegisterWrite(RegApply, ApplyValue) >= 0;

    private string ReadDeviceName()
    {
        var bytes = new byte[16];
        for (var i = 0; i < bytes.Length; i++)
            bytes[i] = (byte)Math.Max(0, RegisterRead((ushort)(RegDeviceName + i)));
        var end = Array.IndexOf(bytes, (byte)0);
        return Encoding.ASCII.GetString(bytes, 0, end < 0 ? bytes.Length : end);
    }

    private OperationResult<bool> Failure() =>
        OperationResult<bool>.Failure($"{_name} did not accept the write over {_bus.Info.Name}.", ErrorCategory.ServiceUnavailable);

    // ---- ENESMBusInterface_i2c_smbus ----

    internal int RegisterRead(ushort register) => RegisterRead(_bus, _address, register);

    internal static int RegisterRead(II2cBus bus, byte address, ushort register)
    {
        if (bus.WriteWordData(address, 0x00, Swap(register)) < 0)
            return -1;
        return bus.ReadByteData(address, 0x81);
    }

    private int RegisterWrite(ushort register, byte value)
    {
        if (_bus.WriteWordData(_address, 0x00, Swap(register)) < 0)
            return -1;
        return _bus.WriteByteData(_address, 0x01, value);
    }

    private int RegisterWriteBlock(ushort register, ReadOnlySpan<byte> data)
    {
        if (_bus.WriteWordData(_address, 0x00, Swap(register)) < 0)
            return -1;
        if (_bus.WriteBlockData(_address, 0x03, data) >= 0)
            return 0;
        foreach (var value in data)
        {
            if (_bus.WriteByteData(_address, 0x01, value) < 0)
                return -1;
        }
        return 0;
    }

    /// <summary>The register address goes out high byte first: ((reg &lt;&lt; 8) &amp; 0xFF00) | ((reg &gt;&gt; 8) &amp; 0x00FF).</summary>
    private static ushort Swap(ushort register) => (ushort)(((register << 8) & 0xFF00) | ((register >> 8) & 0x00FF));

    /// <summary>
    /// TestForENESMBusController: something answers at the address, registers
    /// 0xA0 to 0xAF count 0 to 15, and the chip is not a Micron memory module
    /// (which shares the address space and the counting registers).
    /// </summary>
    public static bool Probe(II2cBus bus, byte address, List<string> notes)
    {
        ArgumentNullException.ThrowIfNull(bus);
        ArgumentNullException.ThrowIfNull(notes);
        var response = bus.ReadByte(address);
        if (response < 0)
            response = bus.ReadByteData(address, 0x00);
        if (response < 0)
        {
            notes.Add($"ENE SMBus: nothing answers at 0x{address:X2} on {bus.Info.Name}.");
            return false;
        }

        for (var register = 0xA0; register < 0xB0; register++)
        {
            var value = bus.ReadByteData(address, (byte)register);
            if (value != register - 0xA0)
            {
                notes.Add($"ENE SMBus: 0x{address:X2} on {bus.Info.Name} answered but register 0x{register:X2} read {value}, expected {register - 0xA0}.");
                return false;
            }
        }

        var micron = new byte[16];
        for (var i = 0; i < micron.Length; i++)
            micron[i] = (byte)Math.Max(0, RegisterRead(bus, address, (ushort)(RegMicronCheck + i)));
        var end = Array.IndexOf(micron, (byte)0);
        if (Encoding.ASCII.GetString(micron, 0, end < 0 ? micron.Length : end) == "Micron")
        {
            notes.Add($"ENE SMBus: 0x{address:X2} on {bus.Info.Name} is a Micron memory module, skipped.");
            return false;
        }
        return true;
    }

    public void Dispose()
    {
    }
}
