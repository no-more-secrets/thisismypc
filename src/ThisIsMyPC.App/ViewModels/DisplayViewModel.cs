using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ThisIsMyPC.Core.Display;
using ThisIsMyPC.Core.Results;
using ThisIsMyPC.Core.Services;
using ThisIsMyPC.Modules.Display.Models;
using ThisIsMyPC.Modules.Display.Services;

namespace ThisIsMyPC.App.ViewModels;

/// <summary>
/// Display module screen. Controls apply LIVE (the module's documented
/// carve-out from the pending pipeline): sliders write as they move, input
/// switching fires on its button.
/// </summary>
public sealed partial class DisplayViewModel : ViewModelBase
{
    public ObservableCollection<MonitorItemViewModel> Monitors { get; } = [];

    public string? ScanError { get; }
    public bool HasScanError => ScanError is { Length: > 0 };
    public bool HasNoMonitors => Monitors.Count == 0;

    /// <summary>The link toggle only earns screen space with two or more dimmable displays.</summary>
    public bool CanLinkBrightness { get; }

    [ObservableProperty]
    private bool _linkBrightness;

    private bool _syncingLinkedBrightness;

    public DisplayViewModel(DisplayScanData data, IMonitorService monitorService, IPowerService powerService)
    {
        ScanError = data.ScanError;
        var panel = new InternalPanelService(powerService);
        foreach (var device in data.Monitors)
            Monitors.Add(new MonitorItemViewModel(device, monitorService, panel, OnMonitorBrightnessChanged));

        CanLinkBrightness = Monitors.Count(m => m.SupportsDdc) >= 2;
    }

    /// <summary>
    /// Linked mode moves every dimmable display to the same fraction of its
    /// own range, so a 0-100 monitor and a 0-255 one stay visually together.
    /// </summary>
    private void OnMonitorBrightnessChanged(MonitorItemViewModel source, double value)
    {
        if (!LinkBrightness || _syncingLinkedBrightness)
            return;

        _syncingLinkedBrightness = true;
        try
        {
            var fraction = value / Math.Max(1, source.BrightnessMax);
            foreach (var monitor in Monitors)
            {
                if (monitor != source && monitor.SupportsDdc)
                    monitor.Brightness = Math.Round(fraction * monitor.BrightnessMax);
            }
        }
        finally
        {
            _syncingLinkedBrightness = false;
        }
    }
}

public sealed partial class MonitorItemViewModel : ViewModelBase
{
    private readonly MonitorDevice _device;
    private readonly IMonitorService _monitors;
    private readonly InternalPanelService _panel;
    private readonly Action<MonitorItemViewModel, double>? _brightnessChanged;

    // Latest-wins write coalescing: a moving slider produces values faster
    // than DDC accepts them; one write runs at a time and only the newest
    // queued value survives.
    private int _brightnessWriting;
    private int? _brightnessPending;
    private int _contrastWriting;
    private int? _contrastPending;

    public MonitorItemViewModel(
        MonitorDevice device,
        IMonitorService monitors,
        InternalPanelService panel,
        Action<MonitorItemViewModel, double>? brightnessChanged = null)
    {
        _device = device;
        _monitors = monitors;
        _panel = panel;
        _brightnessChanged = brightnessChanged;
        _brightness = device.Brightness;
        _contrast = device.Contrast ?? 0;
        _selectedInput = device.InputSources.FirstOrDefault(i => i.Value == device.CurrentInput);
        VendorFeatures = device.VendorFeatures
            .Where(f => f.IsNamed)
            .Select(BuildFeature)
            .ToList();
        AdvancedVendorFeatures = device.VendorFeatures
            .Where(f => !f.IsNamed)
            .Select(BuildFeature)
            .ToList();

        VendorFeatureViewModel BuildFeature(VendorVcpFeature f) => new(
            f,
            write: (code, value) => monitors.SetVcpValue(device.Id, code, value),
            reportError: error => LastError = error);
    }

    /// <summary>Features with a known meaning (blue light filter); always visible.</summary>
    public IReadOnlyList<VendorFeatureViewModel> VendorFeatures { get; }
    public bool HasVendorFeatures => VendorFeatures.Count > 0;

    /// <summary>Unnamed vendor codes; live behind the Advanced expander.</summary>
    public IReadOnlyList<VendorFeatureViewModel> AdvancedVendorFeatures { get; }
    public bool HasAdvancedVendorFeatures => AdvancedVendorFeatures.Count > 0;

    public string Name => _device.Name;
    public bool IsInternalPanel => _device.IsInternalPanel;
    public bool SupportsDdc => _device.SupportsDdc;
    public string? DdcError => _device.DdcError;
    public bool HasDdcError => DdcError is { Length: > 0 };

    public double BrightnessMax => _device.BrightnessMax;
    public bool HasContrast => _device.Contrast is not null;
    public double ContrastMax => _device.ContrastMax;
    public bool HasInputSources => _device.InputSources.Count > 0 && !_device.IsInternalPanel;
    public IReadOnlyList<MonitorInputSource> InputSources => _device.InputSources;

    [ObservableProperty]
    private double _brightness;

    [ObservableProperty]
    private double _contrast;

    [ObservableProperty]
    private MonitorInputSource? _selectedInput;

    [ObservableProperty]
    private string _lastError = string.Empty;

    public bool HasError => LastError.Length > 0;

    public bool CanTurnOffScreen => _device.PowerOffValue is not null && !_device.IsInternalPanel;

    partial void OnBrightnessChanged(double value)
    {
        // Typed values can land outside the range; re-set to the clamped
        // value and let that change run the real path.
        var clamped = Math.Clamp(value, 0, BrightnessMax);
        if (clamped != value)
        {
            Brightness = clamped;
            return;
        }

        _brightnessChanged?.Invoke(this, value);
        _ = PushAsync((int)value, brightness: true);
    }

    partial void OnContrastChanged(double value)
    {
        var clamped = Math.Clamp(value, 0, ContrastMax);
        if (clamped != value)
        {
            Contrast = clamped;
            return;
        }

        _ = PushAsync((int)value, brightness: false);
    }

    partial void OnLastErrorChanged(string value) => OnPropertyChanged(nameof(HasError));

    [RelayCommand]
    private async Task SetInputAsync()
    {
        if (SelectedInput is not { } input)
            return;

        var result = await Task.Run(() => _monitors.SetInputSource(_device.Id, input.Value)).ConfigureAwait(true);
        LastError = result.IsSuccess ? string.Empty : result.ErrorMessage ?? "Input switch failed.";
    }

    [RelayCommand]
    private async Task TurnOffScreenAsync()
    {
        if (_device.PowerOffValue is not { } off)
            return;

        var result = await Task.Run(() => _monitors.SetVcpValue(_device.Id, 0xD6, off)).ConfigureAwait(true);
        LastError = result.IsSuccess ? string.Empty : result.ErrorMessage ?? "Screen off failed.";
    }

    private async Task PushAsync(int value, bool brightness)
    {
        if (brightness)
            _brightnessPending = value;
        else
            _contrastPending = value;

        var alreadyWriting = brightness
            ? Interlocked.Exchange(ref _brightnessWriting, 1)
            : Interlocked.Exchange(ref _contrastWriting, 1);
        if (alreadyWriting == 1)
            return; // the running loop picks up the pending value

        try
        {
            while ((brightness ? _brightnessPending : _contrastPending) is { } next)
            {
                if (brightness)
                    _brightnessPending = null;
                else
                    _contrastPending = null;

                var result = await Task.Run(() => Write(next, brightness)).ConfigureAwait(true);
                LastError = result.IsSuccess ? string.Empty : result.ErrorMessage ?? "Write failed.";

                // Pace the stream: monitors wedge their DDC processing when
                // writes arrive back to back (Twinkle Tray throttles too).
                await Task.Delay(60).ConfigureAwait(true);
            }
        }
        finally
        {
            if (brightness)
                _brightnessWriting = 0;
            else
                _contrastWriting = 0;
        }
    }

    private OperationResult<bool> Write(int value, bool brightness)
    {
        if (_device.IsInternalPanel)
            return _panel.SetBrightness(value);

        return brightness
            ? _monitors.SetBrightness(_device.Id, value)
            : _monitors.SetContrast(_device.Id, value);
    }
}

/// <summary>
/// One vendor VCP feature row. Contiguous value lists (0 1 2 3 4) render as a
/// snapping slider; gappy lists as a combo. Writes are live like everything
/// else on this page.
/// </summary>
public sealed partial class VendorFeatureViewModel : ViewModelBase
{
    private readonly VendorVcpFeature _feature;
    private readonly Func<int, int, OperationResult<bool>> _write;
    private readonly Action<string> _reportError;
    private int _writing;
    private int? _pending;

    public VendorFeatureViewModel(
        VendorVcpFeature feature,
        Func<int, int, OperationResult<bool>> write,
        Action<string> reportError)
    {
        _feature = feature;
        _write = write;
        _reportError = reportError;
        _value = feature.Current ?? feature.Values[0];
        _selectedValue = feature.Values.Contains((int)_value) ? (int)_value : feature.Values[0];
        IsSlider = IsContiguous(feature.Values);
    }

    public string Name => _feature.Name;
    public string? Hint => _feature.Hint;
    public bool IsSlider { get; }
    public bool IsCombo => !IsSlider;
    public double Minimum => _feature.Values[0];
    public double Maximum => _feature.Values[^1];
    public IReadOnlyList<int> Values => _feature.Values;

    [ObservableProperty]
    private double _value;

    [ObservableProperty]
    private int _selectedValue;

    partial void OnValueChanged(double value) => _ = PushAsync((int)value);

    partial void OnSelectedValueChanged(int value) => _ = PushAsync(value);

    private async Task PushAsync(int value)
    {
        _pending = value;
        if (Interlocked.Exchange(ref _writing, 1) == 1)
            return;

        try
        {
            while (_pending is { } next)
            {
                _pending = null;
                var result = await Task.Run(() => _write(_feature.Code, next)).ConfigureAwait(true);
                _reportError(result.IsSuccess ? string.Empty : result.ErrorMessage ?? "Write failed.");

                // Same DDC pacing as the brightness/contrast coalescer.
                await Task.Delay(60).ConfigureAwait(true);
            }
        }
        finally
        {
            _writing = 0;
        }
    }

    private static bool IsContiguous(IReadOnlyList<int> values)
    {
        for (var i = 1; i < values.Count; i++)
        {
            if (values[i] != values[i - 1] + 1)
                return false;
        }

        return values.Count >= 2;
    }
}
