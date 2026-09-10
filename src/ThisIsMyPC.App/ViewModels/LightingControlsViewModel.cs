using System.Collections.ObjectModel;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ThisIsMyPC.Core.Hardware.Lighting;
using ThisIsMyPC.Core.Results;

namespace ThisIsMyPC.App.ViewModels;

/// <summary>
/// The Lighting tab's controls: every device the lighting backend exposes
/// (the built-in controllers), with its mode, brightness, speed, direction
/// and colors. Controls apply
/// live, the Display module's carve-out: a color is its own undo and Windows
/// persists nothing. Every write first asks <c>writesAllowed</c>, which reads
/// the tab's current decision, so the Settings override that shows these
/// controls can never make them write; <see cref="WritesAllowed"/> mirrors
/// that answer for the view, which greys the cards out.
/// </summary>
public sealed partial class LightingControlsViewModel : ViewModelBase, IDisposable
{
    private readonly ILightingBackend _backend;
    private readonly Func<bool> _writeGate;
    private ILightingSession? _session;
    private bool _disposed;
    private bool _reloadRequested;

    public ObservableCollection<LightingDeviceViewModel> Devices { get; } = [];

    [ObservableProperty]
    private string? _status;

    [ObservableProperty]
    private bool _isLoading;

    /// <summary>The tab's decision permits device writes; the view disables the cards otherwise.</summary>
    [ObservableProperty]
    private bool _writesAllowed;

    public bool HasDevices => Devices.Count > 0;

    public LightingControlsViewModel(ILightingBackend backend, Func<bool> writesAllowed)
    {
        ArgumentNullException.ThrowIfNull(backend);
        ArgumentNullException.ThrowIfNull(writesAllowed);
        _backend = backend;
        _writeGate = writesAllowed;
        // The observable copy starts from the gate so the first render is right.
        _writesAllowed = writesAllowed();
    }

    /// <summary>
    /// Opens a session (once) and reads every device. A call during a load is not
    /// lost: the load runs again when it finishes, so a device-list change
    /// announced mid-read still lands. A dead session is replaced.
    /// </summary>
    public async Task LoadAsync()
    {
        if (_disposed)
            return;
        if (IsLoading)
        {
            _reloadRequested = true;
            return;
        }
        IsLoading = true;
        try
        {
            do
            {
                _reloadRequested = false;
                await LoadOnceAsync().ConfigureAwait(true);
            }
            while (_reloadRequested && !_disposed);
        }
        finally
        {
            IsLoading = false;
            OnPropertyChanged(nameof(HasDevices));
        }
    }

    private async Task LoadOnceAsync()
    {
        Status = "Reading lighting devices...";
        if (_session is { IsConnected: false })
            DropSession();
        if (_session is null)
        {
            var connect = await _backend.OpenAsync().ConfigureAwait(true);
            if (!connect.IsSuccess || connect.Value is null)
            {
                Status = connect.ErrorMessage;
                return;
            }
            if (_disposed)
            {
                connect.Value.Dispose();
                return;
            }
            _session = connect.Value;
            _session.DeviceListChanged += OnDeviceListChanged;
        }

        var devices = await _session.GetDevicesAsync().ConfigureAwait(true);
        if (_disposed)
            return;
        if (!devices.IsSuccess || devices.Value is null)
        {
            Status = devices.ErrorMessage;
            return;
        }

        foreach (var old in Devices)
            old.Dispose();
        Devices.Clear();
        foreach (var device in devices.Value)
            Devices.Add(new LightingDeviceViewModel(device, _session, _writeGate));
        OnPropertyChanged(nameof(HasDevices));
        Status = Devices.Count == 0 ? "No supported lighting device answered on this PC." : null;
    }

    [RelayCommand]
    private Task ReloadAsync() => LoadAsync();

    private void OnDeviceListChanged(object? sender, EventArgs e) =>
        Dispatcher.UIThread.Post(() => _ = LoadAsync());

    private void DropSession()
    {
        if (_session is null)
            return;
        _session.DeviceListChanged -= OnDeviceListChanged;
        _session.Dispose();
        _session = null;
    }

    public void Dispose()
    {
        if (_disposed)
            return;
        _disposed = true;
        foreach (var device in Devices)
            device.Dispose();
        DropSession();
    }
}

/// <summary>One device card. Mode changes and slider moves write through the session, latest wins.</summary>
public sealed partial class LightingDeviceViewModel : ViewModelBase, IDisposable
{
    private readonly ILightingSession _session;
    private readonly Func<bool> _writesAllowed;
    private readonly LatestWriteQueue _writes;
    private LightingDevice _device;
    private LightingMode _mode;
    private bool _syncing;

    public LightingDeviceViewModel(LightingDevice device, ILightingSession session, Func<bool> writesAllowed)
    {
        ArgumentNullException.ThrowIfNull(device);
        _device = device;
        _session = session;
        _writesAllowed = writesAllowed;
        _writes = new LatestWriteQueue(error => LastError = error);
        _mode = device.ActiveMode ?? device.Modes.FirstOrDefault() ?? new LightingMode { Index = 0, Name = "Default" };
        _selectedMode = _mode;
        Colors = [];
        SyncFromMode(readColorsFromDevice: true);
    }

    public int Index => _device.Index;
    public string Name => _device.Name;
    public string TypeName => LightingDevice.DescribeType(_device.Type);

    /// <summary>Vendor, type, and the LED count on one line.</summary>
    public string Subtitle
    {
        get
        {
            var zones = _device.Zones.Count;
            var leds = _device.Leds.Count;
            var ledText = leds == 1 ? "1 LED" : $"{leds} LEDs";
            var count = zones == 1 ? ledText : $"{zones} zones, {ledText}";
            return string.IsNullOrWhiteSpace(_device.Vendor)
                ? $"{TypeName}, {count}"
                : $"{_device.Vendor}, {TypeName}, {count}";
        }
    }

    public IReadOnlyList<LightingMode> Modes => _device.Modes;

    [ObservableProperty]
    private LightingMode _selectedMode;

    [ObservableProperty]
    private double _brightness;

    [ObservableProperty]
    private double _speed;

    [ObservableProperty]
    private LightingDirectionOption? _selectedDirection;

    [ObservableProperty]
    private bool _randomColors;

    [ObservableProperty]
    private string? _lastError;

    public ObservableCollection<ColorSlotViewModel> Colors { get; }

    public bool HasBrightness => _mode.HasBrightness;
    public double BrightnessMin => _mode.BrightnessRange.Low;
    public double BrightnessMax => _mode.BrightnessRange.High;
    public bool HasSpeed => _mode.HasSpeed;
    public double SpeedMin => _mode.SpeedRange.Low;
    public double SpeedMax => _mode.SpeedRange.High;
    public bool HasDirection => _mode.HasDirection;
    public IReadOnlyList<LightingDirectionOption> Directions => _mode.Directions.Select(LightingDirectionOption.For).ToList();
    public bool HasRandomColor => _mode.HasRandomColor;
    public bool HasColors => Colors.Count > 0 && !RandomColors;
    public bool CanSave => _mode.CanSave;
    public bool HasLastError => !string.IsNullOrEmpty(LastError);

    /// <summary>Whether the tab lets this card write right now (the policy's decision, not the override).</summary>
    public bool WritesAllowed => _writesAllowed();

    partial void OnSelectedModeChanged(LightingMode value)
    {
        if (_syncing || value is null)
            return;
        _mode = value;
        SyncFromMode(readColorsFromDevice: false);
        Write(async () =>
        {
            var result = await _session.SetModeAsync(Index, CurrentMode()).ConfigureAwait(false);
            if (!result.IsSuccess)
                return result;
            // The server may reshape colors on a mode change; read it back.
            var fresh = await _session.GetDeviceAsync(Index).ConfigureAwait(false);
            if (fresh.IsSuccess && fresh.Value is { } device)
                Dispatcher.UIThread.Post(() => ApplyDevice(device));
            return result;
        });
    }

    partial void OnBrightnessChanged(double value)
    {
        if (!_syncing)
            WriteMode();
    }

    partial void OnSpeedChanged(double value)
    {
        if (!_syncing)
            WriteMode();
    }

    partial void OnSelectedDirectionChanged(LightingDirectionOption? value)
    {
        if (!_syncing && value is not null)
            WriteMode();
    }

    partial void OnRandomColorsChanged(bool value)
    {
        OnPropertyChanged(nameof(HasColors));
        if (!_syncing)
            WriteMode();
    }

    /// <summary>The mode with this card's current settings folded in.</summary>
    private LightingMode CurrentMode()
    {
        var colorMode = _mode.ColorMode;
        if (_mode.HasRandomColor && RandomColors)
            colorMode = LightingColorMode.Random;
        else if (colorMode == LightingColorMode.Random)
            colorMode = _mode.HasModeSpecificColor ? LightingColorMode.ModeSpecific : _mode.HasPerLedColor ? LightingColorMode.PerLed : LightingColorMode.None;

        var colors = colorMode == LightingColorMode.ModeSpecific
            ? Colors.Where(c => c.Kind == ColorSlotKind.ModeColor).Select(c => c.Color).ToList()
            : _mode.Colors;

        return _mode with
        {
            Brightness = _mode.HasBrightness ? (uint)Math.Round(Math.Clamp(Brightness, BrightnessMin, BrightnessMax)) : _mode.Brightness,
            Speed = _mode.HasSpeed ? (uint)Math.Round(Math.Clamp(Speed, SpeedMin, SpeedMax)) : _mode.Speed,
            Direction = SelectedDirection?.Value ?? _mode.Direction,
            ColorMode = colorMode,
            Colors = colors,
        };
    }

    private void WriteMode() => Write(() => _session.SetModeAsync(Index, CurrentMode()));

    /// <summary>A color slot changed: mode colors go through the mode; LED colors go straight to the LEDs.</summary>
    private void OnColorChanged(ColorSlotViewModel slot)
    {
        if (_syncing)
            return;
        switch (slot.Kind)
        {
            case ColorSlotKind.ModeColor:
                WriteMode();
                break;
            case ColorSlotKind.AllLeds:
                foreach (var zone in Colors.Where(c => c.Kind == ColorSlotKind.Zone))
                    zone.SetSilently(slot.Color);
                var all = Enumerable.Repeat(slot.Color, _device.Leds.Count).ToList();
                Write(() => _session.SetLedsAsync(Index, all));
                break;
            case ColorSlotKind.Zone:
                var zone1 = _device.Zones[slot.ZoneIndex];
                var zoneColors = Enumerable.Repeat(slot.Color, (int)zone1.LedCount).ToList();
                Write(() => _session.SetZoneLedsAsync(Index, slot.ZoneIndex, zoneColors));
                break;
        }
    }

    [RelayCommand]
    private Task SaveAsync()
    {
        if (!WritesAllowed)
        {
            LastError = "Lighting writes are not permitted on this PC.";
            return Task.CompletedTask;
        }
        return _writes.RunAsync(() => _session.SaveModeAsync(Index, CurrentMode()));
    }

    private void Write(Func<Task<OperationResult<bool>>> write)
    {
        if (!WritesAllowed)
        {
            LastError = "Lighting writes are not permitted on this PC.";
            return;
        }
        LastError = null;
        _writes.Post(write);
    }

    /// <summary>Folds a fresh read of the device into the card without writing anything back.</summary>
    public void ApplyDevice(LightingDevice device)
    {
        ArgumentNullException.ThrowIfNull(device);
        var modeListChanged = !device.Modes.Select(m => m.Name).SequenceEqual(_device.Modes.Select(m => m.Name));
        _device = device;
        _mode = device.ActiveMode ?? _mode;
        _syncing = true;
        try
        {
            // The list first: swapping a ComboBox's items clears its selection,
            // and that clear must not land on the selection set next.
            if (modeListChanged)
                OnPropertyChanged(nameof(Modes));
            SelectedMode = Modes.FirstOrDefault(m => m.Index == _mode.Index) ?? _mode;
        }
        finally
        {
            _syncing = false;
        }
        SyncFromMode(readColorsFromDevice: true);
        OnPropertyChanged(nameof(Subtitle));
    }

    /// <summary>
    /// Rebuilds the rows for <see cref="_mode"/>. Ranges are announced before
    /// the values: a slider whose range moves clamps its value and writes the
    /// clamped number back, and that write-back must land on the old value,
    /// not on the one this mode carries.
    /// </summary>
    private void SyncFromMode(bool readColorsFromDevice)
    {
        _syncing = true;
        try
        {
            foreach (var name in new[]
            {
                nameof(HasBrightness), nameof(BrightnessMin), nameof(BrightnessMax),
                nameof(HasSpeed), nameof(SpeedMin), nameof(SpeedMax),
                nameof(HasDirection), nameof(Directions), nameof(HasRandomColor), nameof(CanSave),
            })
            {
                OnPropertyChanged(name);
            }

            Brightness = _mode.Brightness;
            Speed = _mode.Speed;
            SelectedDirection = _mode.HasDirection ? Directions.FirstOrDefault(d => d.Value == _mode.Direction) ?? Directions.FirstOrDefault() : null;
            RandomColors = _mode.ColorMode == LightingColorMode.Random;
            foreach (var slot in Colors)
                slot.Changed -= OnColorChanged;
            Colors.Clear();

            if (_mode.ColorMode == LightingColorMode.ModeSpecific || (_mode.HasModeSpecificColor && _mode.ColorMode != LightingColorMode.PerLed))
            {
                var count = _mode.Colors.Count;
                for (var i = 0; i < count; i++)
                    Colors.Add(new ColorSlotViewModel(count == 1 ? "Color" : $"Color {i + 1}", ColorSlotKind.ModeColor, _mode.Colors[i]));
            }
            else if (_mode.HasPerLedColor)
            {
                var first = _device.Colors.Count > 0 ? _device.Colors[0] : RgbColor.Black;
                Colors.Add(new ColorSlotViewModel("All LEDs", ColorSlotKind.AllLeds, first));
                if (_device.Zones.Count > 1)
                {
                    var start = 0;
                    for (var z = 0; z < _device.Zones.Count; z++)
                    {
                        var zone = _device.Zones[z];
                        var color = readColorsFromDevice && start < _device.Colors.Count ? _device.Colors[start] : first;
                        Colors.Add(new ColorSlotViewModel(zone.Name, ColorSlotKind.Zone, color) { ZoneIndex = z });
                        start += (int)zone.LedCount;
                    }
                }
            }
            foreach (var slot in Colors)
                slot.Changed += OnColorChanged;
        }
        finally
        {
            _syncing = false;
        }
        OnPropertyChanged(nameof(HasColors));
    }

    partial void OnLastErrorChanged(string? value) => OnPropertyChanged(nameof(HasLastError));

    public void Dispose()
    {
        foreach (var slot in Colors)
            slot.Changed -= OnColorChanged;
    }
}

/// <summary>A direction with product copy for the combo box.</summary>
public sealed record LightingDirectionOption(LightingDirection Value, string Label)
{
    public static LightingDirectionOption For(LightingDirection direction) => new(direction, direction switch
    {
        LightingDirection.Left => "Left",
        LightingDirection.Right => "Right",
        LightingDirection.Up => "Up",
        LightingDirection.Down => "Down",
        LightingDirection.Horizontal => "Horizontal",
        LightingDirection.Vertical => "Vertical",
        LightingDirection.UpLeft => "Up left",
        LightingDirection.UpRight => "Up right",
        LightingDirection.DownLeft => "Down left",
        LightingDirection.DownRight => "Down right",
        _ => direction.ToString(),
    });

    public override string ToString() => Label;
}

public enum ColorSlotKind
{
    /// <summary>One of the mode's own colors (breathing color, rainbow base).</summary>
    ModeColor,

    /// <summary>Every LED of the device.</summary>
    AllLeds,

    /// <summary>Every LED of one zone.</summary>
    Zone,
}

/// <summary>One color the person can set: a swatch, a hex box and three channel sliders, all views of the same value.</summary>
public sealed partial class ColorSlotViewModel : ViewModelBase
{
    private bool _syncing;

    public ColorSlotViewModel(string label, ColorSlotKind kind, RgbColor color)
    {
        Label = label;
        Kind = kind;
        _color = color;
        _red = color.R;
        _green = color.G;
        _blue = color.B;
        _hex = color.ToHex();
    }

    public string Label { get; }
    public ColorSlotKind Kind { get; }
    public int ZoneIndex { get; init; }

    /// <summary>Raised after the person changed the color, never after a silent sync.</summary>
    public event Action<ColorSlotViewModel>? Changed;

    [ObservableProperty]
    private RgbColor _color;

    [ObservableProperty]
    private double _red;

    [ObservableProperty]
    private double _green;

    [ObservableProperty]
    private double _blue;

    [ObservableProperty]
    private string _hex;

    /// <summary>The swatch color; the view owns the brush.</summary>
    public Avalonia.Media.Color SwatchColor => Avalonia.Media.Color.FromRgb(Color.R, Color.G, Color.B);

    /// <summary>Sets the color from a device read or a sibling slot without raising Changed.</summary>
    public void SetSilently(RgbColor color) => Set(color, notify: false);

    private void Set(RgbColor color, bool notify)
    {
        var wasSyncing = _syncing;
        _syncing = true;
        try
        {
            Color = color;
            Red = color.R;
            Green = color.G;
            Blue = color.B;
            Hex = color.ToHex();
        }
        finally
        {
            _syncing = wasSyncing;
        }
        OnPropertyChanged(nameof(SwatchColor));
        if (notify)
            Changed?.Invoke(this);
    }

    partial void OnRedChanged(double value) => FromChannels();
    partial void OnGreenChanged(double value) => FromChannels();
    partial void OnBlueChanged(double value) => FromChannels();

    private void FromChannels()
    {
        if (_syncing)
            return;
        Set(new RgbColor(Clamp(Red), Clamp(Green), Clamp(Blue)), notify: true);
    }

    partial void OnHexChanged(string value)
    {
        if (_syncing)
            return;
        if (RgbColor.TryParseHex(value) is { } parsed && parsed != Color)
            Set(parsed, notify: true);
    }

    private static byte Clamp(double value) => (byte)Math.Clamp(Math.Round(value), 0, 255);
}
