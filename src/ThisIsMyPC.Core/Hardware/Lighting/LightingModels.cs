using System.Globalization;

namespace ThisIsMyPC.Core.Hardware.Lighting;

/// <summary>One LED color. On the OpenRGB wire it is a 32-bit value laid out R, G, B, 0.</summary>
public readonly record struct RgbColor(byte R, byte G, byte B)
{
    public static RgbColor Black { get; } = new(0, 0, 0);

    public static RgbColor FromWire(uint value) => new((byte)value, (byte)(value >> 8), (byte)(value >> 16));

    public uint ToWire() => R | ((uint)G << 8) | ((uint)B << 16);

    /// <summary>#RRGGBB, upper case.</summary>
    public string ToHex() => string.Create(CultureInfo.InvariantCulture, $"#{R:X2}{G:X2}{B:X2}");

    /// <summary>Accepts RRGGBB with or without a leading #; anything else is null.</summary>
    public static RgbColor? TryParseHex(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return null;
        var span = text.AsSpan().Trim();
        if (span.Length > 0 && span[0] == '#')
            span = span[1..];
        if (span.Length != 6 || !uint.TryParse(span, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var packed))
            return null;
        return new RgbColor((byte)(packed >> 16), (byte)(packed >> 8), (byte)packed);
    }
}

/// <summary>DEVICE_TYPE values from RGBControllerInterface.h, in wire order.</summary>
public enum LightingDeviceType
{
    Motherboard,
    Dram,
    Gpu,
    Cooler,
    LedStrip,
    Keyboard,
    Mouse,
    MouseMat,
    Headset,
    HeadsetStand,
    Gamepad,
    Light,
    Speaker,
    Virtual,
    Storage,
    Case,
    Microphone,
    Accessory,
    Keypad,
    Laptop,
    Monitor,
    Unknown,
}

/// <summary>ZONE_TYPE values, in wire order.</summary>
public enum LightingZoneType
{
    Single,
    Linear,
    Matrix,
    LinearLoop,
    MatrixLoopX,
    MatrixLoopY,
    Segmented,
}

/// <summary>MODE_FLAG bits.</summary>
[Flags]
public enum LightingModeFlags : uint
{
    None = 0,
    HasSpeed = 1 << 0,
    HasDirectionLeftRight = 1 << 1,
    HasDirectionUpDown = 1 << 2,
    HasDirectionHorizontalVertical = 1 << 3,
    HasBrightness = 1 << 4,
    HasPerLedColor = 1 << 5,
    HasModeSpecificColor = 1 << 6,
    HasRandomColor = 1 << 7,
    ManualSave = 1 << 8,
    AutomaticSave = 1 << 9,
    RequiresEntireDevice = 1 << 10,
    HasDirectionDiagonal = 1 << 11,
}

/// <summary>MODE_COLORS values.</summary>
public enum LightingColorMode
{
    None = 0,
    PerLed = 1,
    ModeSpecific = 2,
    Random = 3,
}

/// <summary>MODE_DIRECTION values.</summary>
public enum LightingDirection
{
    Left = 0,
    Right = 1,
    Up = 2,
    Down = 3,
    Horizontal = 4,
    Vertical = 5,
    UpLeft = 6,
    UpRight = 7,
    DownLeft = 8,
    DownRight = 9,
}

/// <summary>One mode a device offers, with its current settings. Ranges are inclusive.</summary>
public sealed record LightingMode
{
    public required int Index { get; init; }
    public required string Name { get; init; }
    public int Value { get; init; }
    public LightingModeFlags Flags { get; init; }
    public uint SpeedMin { get; init; }
    public uint SpeedMax { get; init; }
    public uint BrightnessMin { get; init; }
    public uint BrightnessMax { get; init; }
    public uint ColorsMin { get; init; }
    public uint ColorsMax { get; init; }
    public uint Speed { get; init; }
    public uint Brightness { get; init; }
    public LightingDirection Direction { get; init; }
    public LightingColorMode ColorMode { get; init; }
    public IReadOnlyList<RgbColor> Colors { get; init; } = [];

    /// <summary>Some devices report the range backwards (min 4, max 0: a slower value is a larger number); the range is still real.</summary>
    public bool HasSpeed => Flags.HasFlag(LightingModeFlags.HasSpeed) && SpeedMax != SpeedMin;
    public bool HasBrightness => Flags.HasFlag(LightingModeFlags.HasBrightness) && BrightnessMax != BrightnessMin;

    /// <summary>The speed range in slider order (low, high), whichever way the device reported it.</summary>
    public (uint Low, uint High) SpeedRange => SpeedMin <= SpeedMax ? (SpeedMin, SpeedMax) : (SpeedMax, SpeedMin);

    public (uint Low, uint High) BrightnessRange => BrightnessMin <= BrightnessMax ? (BrightnessMin, BrightnessMax) : (BrightnessMax, BrightnessMin);
    public bool HasPerLedColor => Flags.HasFlag(LightingModeFlags.HasPerLedColor);
    public bool HasModeSpecificColor => Flags.HasFlag(LightingModeFlags.HasModeSpecificColor);
    public bool HasRandomColor => Flags.HasFlag(LightingModeFlags.HasRandomColor);
    public bool CanSave => Flags.HasFlag(LightingModeFlags.ManualSave);

    public bool HasDirection =>
        (Flags & (LightingModeFlags.HasDirectionLeftRight | LightingModeFlags.HasDirectionUpDown
            | LightingModeFlags.HasDirectionHorizontalVertical | LightingModeFlags.HasDirectionDiagonal)) != 0;

    /// <summary>The directions this mode accepts, in wire order.</summary>
    public IReadOnlyList<LightingDirection> Directions
    {
        get
        {
            var list = new List<LightingDirection>();
            if (Flags.HasFlag(LightingModeFlags.HasDirectionLeftRight))
                list.AddRange([LightingDirection.Left, LightingDirection.Right]);
            if (Flags.HasFlag(LightingModeFlags.HasDirectionUpDown))
                list.AddRange([LightingDirection.Up, LightingDirection.Down]);
            if (Flags.HasFlag(LightingModeFlags.HasDirectionHorizontalVertical))
                list.AddRange([LightingDirection.Horizontal, LightingDirection.Vertical]);
            if (Flags.HasFlag(LightingModeFlags.HasDirectionDiagonal))
                list.AddRange([LightingDirection.UpLeft, LightingDirection.UpRight, LightingDirection.DownLeft, LightingDirection.DownRight]);
            return list;
        }
    }
}

public sealed record LightingSegment(string Name, LightingZoneType Type, uint StartIndex, uint LedCount);

/// <summary>A zone: a run of LEDs the device addresses together.</summary>
public sealed record LightingZone
{
    public required int Index { get; init; }
    public required string Name { get; init; }
    public LightingZoneType Type { get; init; }
    public uint LedsMin { get; init; }
    public uint LedsMax { get; init; }
    public uint LedCount { get; init; }
    public uint MatrixHeight { get; init; }
    public uint MatrixWidth { get; init; }
    public IReadOnlyList<LightingSegment> Segments { get; init; } = [];
    public uint Flags { get; init; }
}

public sealed record LightingLed(string Name, uint Value);

/// <summary>One controller the OpenRGB server exposes, as read at one moment.</summary>
public sealed record LightingDevice
{
    /// <summary>Position in the server's controller list; the id every write is addressed to.</summary>
    public required int Index { get; init; }
    public LightingDeviceType Type { get; init; }
    public required string Name { get; init; }
    public string Vendor { get; init; } = string.Empty;
    public string Description { get; init; } = string.Empty;
    public string Version { get; init; } = string.Empty;
    public string Serial { get; init; } = string.Empty;
    public string Location { get; init; } = string.Empty;
    public int ActiveModeIndex { get; init; }
    public IReadOnlyList<LightingMode> Modes { get; init; } = [];
    public IReadOnlyList<LightingZone> Zones { get; init; } = [];
    public IReadOnlyList<LightingLed> Leds { get; init; } = [];
    public IReadOnlyList<RgbColor> Colors { get; init; } = [];

    public LightingMode? ActiveMode =>
        ActiveModeIndex >= 0 && ActiveModeIndex < Modes.Count ? Modes[ActiveModeIndex] : null;

    /// <summary>Product copy for the device type.</summary>
    public static string DescribeType(LightingDeviceType type) => type switch
    {
        LightingDeviceType.Motherboard => "Motherboard",
        LightingDeviceType.Dram => "Memory",
        LightingDeviceType.Gpu => "Graphics card",
        LightingDeviceType.Cooler => "Cooler",
        LightingDeviceType.LedStrip => "LED strip",
        LightingDeviceType.Keyboard => "Keyboard",
        LightingDeviceType.Mouse => "Mouse",
        LightingDeviceType.MouseMat => "Mouse mat",
        LightingDeviceType.Headset => "Headset",
        LightingDeviceType.HeadsetStand => "Headset stand",
        LightingDeviceType.Gamepad => "Gamepad",
        LightingDeviceType.Light => "Light",
        LightingDeviceType.Speaker => "Speaker",
        LightingDeviceType.Virtual => "Virtual device",
        LightingDeviceType.Storage => "Storage",
        LightingDeviceType.Case => "Case",
        LightingDeviceType.Microphone => "Microphone",
        LightingDeviceType.Accessory => "Accessory",
        LightingDeviceType.Keypad => "Keypad",
        LightingDeviceType.Laptop => "Laptop",
        LightingDeviceType.Monitor => "Monitor",
        _ => "Device",
    };
}
