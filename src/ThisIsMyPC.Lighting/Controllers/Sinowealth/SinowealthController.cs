using System.Text;
using ThisIsMyPC.Core.Hardware.Lighting;
using ThisIsMyPC.Core.Results;
using ThisIsMyPC.Lighting.Detection;
using ThisIsMyPC.Lighting.Transport;

namespace ThisIsMyPC.Lighting.Controllers.Sinowealth;

/// <summary>
/// Sinowealth-firmware mice (Glorious Model O and D, Everest GT-100). Port
/// of OpenRGB's SinowealthController and RGBController_Sinowealth. The mouse
/// exposes a configuration blob as feature report 4 (520 bytes); every
/// change reads it back through a command on a second collection, patches
/// the mode bytes, and writes the blob. The mouse stores each write itself,
/// so every mode is automatically saved.
/// </summary>
public sealed class SinowealthController : ILightingController
{
    internal const int ConfigSize = 167;
    internal const int ConfigSizeMin = 131;
    internal const int ConfigReportSize = 520;
    internal const int CommandReportSize = 6;

    private const byte ModeOff = 0x00;
    private const byte ModeRainbow = 0x01;
    private const byte ModeStatic = 0x02;
    private const byte ModeSpectrumBreathing = 0x03;
    private const byte ModeTail = 0x04;
    private const byte ModeSpectrumCycle = 0x05;
    private const byte ModeRave = 0x07;
    private const byte ModeEpilepsy = 0x08;
    private const byte ModeWave = 0x09;
    private const byte ModeBreathing = 0x0A;

    private const uint SpeedSlow = 0x01;
    private const uint SpeedNormal = 0x02;
    private const uint SpeedFast = 0x03;
    private const uint BrightnessLow = 0x01;
    private const uint BrightnessNormal = 0x02;
    private const uint BrightnessHigh = 0x04;
    private const byte DirectionDown = 0x00;
    private const byte DirectionUp = 0x01;

    private readonly IHidDevice _data;
    private readonly IHidDevice _command;
    private readonly string _name;
    private readonly string _location;
    private readonly string _serial;
    private readonly string _version;
    private readonly List<LightingMode> _modes;
    private readonly byte[] _configuration = new byte[ConfigReportSize];
    private int _activeMode;
    private RgbColor _color = new(255, 255, 255);

    public SinowealthController(IHidDevice data, IHidDevice command, string name, string location)
    {
        ArgumentNullException.ThrowIfNull(data);
        ArgumentNullException.ThrowIfNull(command);
        _data = data;
        _command = command;
        _name = name;
        _location = location;
        _serial = command.Info.SerialNumber ?? string.Empty;
        _version = ReadFirmwareVersion();
        _modes = BuildModes();
    }

    public string Family => "Sinowealth";

    private static List<LightingMode> BuildModes()
    {
        const LightingModeFlags save = LightingModeFlags.AutomaticSave;
        var modes = new List<LightingMode>
        {
            new()
            {
                Index = 0, Name = "Static", Value = ModeStatic,
                Flags = LightingModeFlags.HasPerLedColor | LightingModeFlags.HasBrightness | save,
                BrightnessMin = BrightnessLow, BrightnessMax = BrightnessHigh, Brightness = BrightnessNormal,
                ColorMode = LightingColorMode.PerLed,
            },
            new() { Index = 1, Name = "Off", Value = ModeOff, Flags = save, ColorMode = LightingColorMode.None },
            new()
            {
                Index = 2, Name = "Rainbow", Value = ModeRainbow,
                Flags = LightingModeFlags.HasSpeed | LightingModeFlags.HasDirectionUpDown | save,
                SpeedMin = SpeedSlow, SpeedMax = SpeedFast, Speed = SpeedNormal,
                Direction = LightingDirection.Up, ColorMode = LightingColorMode.None,
            },
            new()
            {
                Index = 3, Name = "Seamless Breathing", Value = ModeSpectrumBreathing,
                Flags = LightingModeFlags.HasSpeed | LightingModeFlags.HasModeSpecificColor | save,
                SpeedMin = SpeedSlow, SpeedMax = SpeedFast, Speed = SpeedNormal,
                ColorsMin = 7, ColorsMax = 7, ColorMode = LightingColorMode.ModeSpecific,
                Colors = DefaultColors(7),
            },
            new()
            {
                Index = 4, Name = "Tail", Value = ModeTail,
                Flags = LightingModeFlags.HasSpeed | LightingModeFlags.HasBrightness | save,
                SpeedMin = SpeedSlow, SpeedMax = SpeedFast, Speed = SpeedNormal,
                BrightnessMin = BrightnessLow, BrightnessMax = BrightnessHigh, Brightness = BrightnessNormal,
                ColorMode = LightingColorMode.None,
            },
            new()
            {
                Index = 5, Name = "Spectrum Cycle", Value = ModeSpectrumCycle,
                Flags = LightingModeFlags.HasSpeed | save,
                SpeedMin = SpeedSlow, SpeedMax = SpeedFast, Speed = SpeedNormal,
                ColorMode = LightingColorMode.None,
            },
            new()
            {
                Index = 6, Name = "Rave", Value = ModeRave,
                Flags = LightingModeFlags.HasSpeed | LightingModeFlags.HasBrightness | LightingModeFlags.HasModeSpecificColor | save,
                SpeedMin = SpeedSlow, SpeedMax = SpeedFast, Speed = SpeedNormal,
                BrightnessMin = BrightnessLow, BrightnessMax = BrightnessHigh, Brightness = BrightnessNormal,
                ColorsMin = 2, ColorsMax = 2, ColorMode = LightingColorMode.ModeSpecific,
                Colors = DefaultColors(2),
            },
            new() { Index = 7, Name = "Epilepsy", Value = ModeEpilepsy, Flags = save, ColorMode = LightingColorMode.None },
            new()
            {
                Index = 8, Name = "Wave", Value = ModeWave,
                Flags = LightingModeFlags.HasSpeed | LightingModeFlags.HasBrightness | save,
                SpeedMin = SpeedSlow, SpeedMax = SpeedFast, Speed = SpeedNormal,
                BrightnessMin = BrightnessLow, BrightnessMax = BrightnessHigh, Brightness = BrightnessNormal,
                ColorMode = LightingColorMode.None,
            },
            new()
            {
                Index = 9, Name = "Breathing", Value = ModeBreathing,
                Flags = LightingModeFlags.HasSpeed | LightingModeFlags.HasModeSpecificColor | save,
                SpeedMin = SpeedSlow, SpeedMax = SpeedFast, Speed = SpeedNormal,
                ColorsMin = 1, ColorsMax = 1, ColorMode = LightingColorMode.ModeSpecific,
                Colors = DefaultColors(1),
            },
        };
        return modes;
    }

    private static List<RgbColor> DefaultColors(int count) => Enumerable.Repeat(new RgbColor(255, 255, 255), count).ToList();

    public LightingDevice Describe(int index) => new()
    {
        Index = index,
        Type = LightingDeviceType.Mouse,
        Name = _name,
        Vendor = string.Empty,
        Description = "Sinowealth mouse",
        Version = _version,
        Serial = _serial,
        Location = "HID: " + _location,
        ActiveModeIndex = _activeMode,
        Modes = _modes.ToList(),
        Zones = [new LightingZone { Index = 0, Name = "Mouse", Type = LightingZoneType.Single, LedsMin = 1, LedsMax = 1, LedCount = 1 }],
        Leds = [new LightingLed("Mouse LED", 0)],
        Colors = [_color],
    };

    public OperationResult<bool> SetMode(LightingMode mode)
    {
        ArgumentNullException.ThrowIfNull(mode);
        if (mode.Index < 0 || mode.Index >= _modes.Count)
            return OperationResult<bool>.Failure("The mouse has no such mode.", ErrorCategory.NotFound);
        var stored = _modes[mode.Index];
        _modes[mode.Index] = stored with
        {
            Speed = stored.HasSpeed ? mode.Speed : stored.Speed,
            Brightness = stored.HasBrightness ? mode.Brightness : stored.Brightness,
            Direction = stored.HasDirection ? mode.Direction : stored.Direction,
            Colors = stored.ColorMode == LightingColorMode.ModeSpecific ? Fit(mode.Colors, stored.Colors.Count) : stored.Colors,
        };
        _activeMode = mode.Index;
        return Apply();
    }

    public OperationResult<bool> SetLeds(IReadOnlyList<RgbColor> colors)
    {
        ArgumentNullException.ThrowIfNull(colors);
        if (colors.Count > 0)
            _color = colors[0];
        return Apply();
    }

    public OperationResult<bool> SetZoneLeds(int zoneIndex, IReadOnlyList<RgbColor> colors) => SetLeds(colors);

    /// <summary>The mouse stores every write already; saving is applying.</summary>
    public OperationResult<bool> SaveMode(LightingMode mode) => SetMode(mode);

    private static List<RgbColor> Fit(IReadOnlyList<RgbColor> colors, int count)
    {
        var list = new List<RgbColor>(count);
        for (var i = 0; i < count; i++)
            list.Add(i < colors.Count ? colors[i] : new RgbColor(255, 255, 255));
        return list;
    }

    /// <summary>DeviceUpdateMode: fold the active mode's settings into the configuration blob and send it.</summary>
    private OperationResult<bool> Apply()
    {
        var mode = _modes[_activeMode];
        var speed = mode.HasSpeed ? mode.Speed : SpeedFast;
        var brightness = mode.HasBrightness ? mode.Brightness : BrightnessHigh;
        var direction = mode.HasDirection && mode.Direction == LightingDirection.Up ? DirectionUp : DirectionDown;
        var colors = mode.ColorMode switch
        {
            LightingColorMode.PerLed => [_color],
            LightingColorMode.ModeSpecific => mode.Colors,
            _ => (IReadOnlyList<RgbColor>)[],
        };
        return WriteMode((byte)mode.Value, (byte)speed, (byte)brightness, direction, colors);
    }

    /// <summary>SinowealthController::SetMode. Offsets are the mouse's configuration layout; the color order on the wire is R, B, G.</summary>
    internal OperationResult<bool> WriteMode(byte mode, byte speed, byte brightness, byte direction, IReadOnlyList<RgbColor> colors)
    {
        var read = ReadProfile();
        if (read < ConfigSizeMin)
            return OperationResult<bool>.Failure("The mouse did not return its configuration.", ErrorCategory.ServiceUnavailable);

        var report = new byte[ConfigReportSize];
        Array.Copy(_configuration, report, ConfigSize);
        report[0x03] = 0x7B;
        report[0x06] = 0x00;
        report[0x35] = mode;
        var level = (byte)(((brightness & 0xF) << 4) | (speed & 0xF));
        switch (mode)
        {
            case ModeRainbow:
                report[0x36] = level;
                report[0x37] = direction;
                break;
            case ModeStatic:
                report[0x38] = (byte)((brightness & 0xF) << 4);
                PutColor(report, 0x39, Color(colors, 0));
                break;
            case ModeSpectrumBreathing:
                report[0x3C] = level;
                report[0x3D] = 0x07;
                for (var i = 0; i < 7; i++)
                    PutColor(report, 0x3E + (3 * i), Color(colors, i));
                break;
            case ModeTail:
                report[0x53] = level;
                break;
            case ModeSpectrumCycle:
                report[0x54] = level;
                break;
            case ModeRave:
                report[0x74] = level;
                PutColor(report, 0x75, Color(colors, 0));
                PutColor(report, 0x78, Color(colors, 1));
                break;
            case ModeWave:
                report[0x7C] = level;
                break;
            case ModeBreathing:
                report[0x7D] = level;
                PutColor(report, 0x7E, Color(colors, 0));
                report[0x81] = 0x00;
                break;
            case ModeOff:
                report[0x81] = 0x00;
                break;
        }

        return _data.SendFeatureReport(report) < 0
            ? OperationResult<bool>.Failure("The mouse refused the lighting update.", ErrorCategory.ServiceUnavailable)
            : OperationResult<bool>.Success(true);
    }

    private static RgbColor Color(IReadOnlyList<RgbColor> colors, int index) =>
        index < colors.Count ? colors[index] : new RgbColor(255, 255, 255);

    private static void PutColor(byte[] report, int offset, RgbColor color)
    {
        report[offset] = color.R;
        report[offset + 1] = color.B;
        report[offset + 2] = color.G;
    }

    /// <summary>SinowealthController::GetProfile: ask on the command collection, read report 4 on the data collection.</summary>
    internal int ReadProfile()
    {
        Span<byte> command = stackalloc byte[CommandReportSize];
        command.Clear();
        command[0] = 0x05;
        command[1] = 0x11;
        if (_command.SendFeatureReport(command) != CommandReportSize)
            return -1;
        Array.Clear(_configuration);
        _configuration[0] = 0x04;
        return _data.GetFeatureReport(_configuration);
    }

    private string ReadFirmwareVersion()
    {
        var buffer = new byte[CommandReportSize + 1];
        buffer[0] = 5;
        buffer[1] = 1;
        if (_command.SendFeatureReport(buffer.AsSpan(0, CommandReportSize)) < 0)
            return string.Empty;
        buffer[1] = 0;
        if (_command.GetFeatureReport(buffer.AsSpan(0, CommandReportSize)) < 0)
            return string.Empty;
        var end = Array.IndexOf(buffer, (byte)0, 2);
        return Encoding.ASCII.GetString(buffer, 2, (end < 0 ? buffer.Length : end) - 2);
    }

    public void Dispose()
    {
        _data.Dispose();
        if (!ReferenceEquals(_data, _command))
            _command.Dispose();
    }
}

/// <summary>Detection for the Sinowealth mice: SinowealthControllerDetect.cpp, DetectSinowealthMouse.</summary>
public static class SinowealthDetectors
{
    private const ushort SinowealthVid = 0x258A;
    private const ushort GloriousModelOPid = 0x0036;
    private const ushort GloriousModelDPid = 0x0033;
    private const ushort EverestGt100Pid = 0x0029;
    private const ushort VendorUsagePage = 0xFF00;

    public static IReadOnlyList<HidDetector> Hid { get; } =
    [
        new("Glorious Model O / O-", SinowealthVid, GloriousModelOPid, DetectMouse, UsagePage: VendorUsagePage),
        new("Glorious Model D / D-", SinowealthVid, GloriousModelDPid, DetectMouse, UsagePage: VendorUsagePage),
        new("Everest GT-100 RGB", SinowealthVid, EverestGt100Pid, DetectMouse, UsagePage: VendorUsagePage),
    ];

    /// <summary>
    /// The mouse presents three collections on the vendor usage page. The
    /// detector runs once per collection, so it only proceeds when the
    /// collections left from this one are a whole number of mice (DetectUsages'
    /// remainder rule). It then finds the collection that accepts the read
    /// command and the collection that returns report 4.
    /// </summary>
    internal static ILightingController? DetectMouse(
        IHidTransport transport, HidDeviceInfo info, IReadOnlyList<HidDeviceInfo> remaining, string name, List<string> notes)
    {
        const int collectionsPerMouse = 3;
        var siblings = remaining
            .Where(s => s.VendorId == info.VendorId && s.ProductId == info.ProductId && s.UsagePage == info.UsagePage)
            .ToList();
        if (siblings.Count % collectionsPerMouse != 0)
            return null;

        var opened = new List<IHidDevice>();
        try
        {
            foreach (var sibling in siblings)
            {
                var device = transport.Open(sibling.Path);
                if (device is null)
                    notes.Add($"{name}: could not open {sibling.Path}.");
                else
                    opened.Add(device);
            }

            // First pass: the collection that accepts the read command.
            byte[] readCommand = [0x05, 0x11, 0x00, 0x00, 0x00, 0x00];
            IHidDevice? command = null;
            foreach (var device in opened)
            {
                if (device.SendFeatureReport(readCommand) > -1)
                {
                    command = device;
                    break;
                }
            }
            if (command is null)
            {
                notes.Add($"{name}: no collection accepted the configuration command.");
                return null;
            }

            // Second pass, from the first collection again ("because
            // Windows"): the one that returns report 4 after that command.
            var configuration = new byte[SinowealthController.ConfigReportSize];
            IHidDevice? data = null;
            foreach (var device in opened)
            {
                Array.Clear(configuration);
                configuration[0] = 0x04;
                if (device.GetFeatureReport(configuration) > -1)
                {
                    data = device;
                    break;
                }
            }
            if (data is null)
            {
                notes.Add($"{name}: no collection returned the configuration report.");
                return null;
            }

            opened.Remove(data);
            opened.Remove(command);
            return new SinowealthController(data, command, name, info.Path);
        }
        finally
        {
            foreach (var device in opened)
                device.Dispose();
        }
    }
}
