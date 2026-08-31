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

    public DisplayViewModel(DisplayScanData data, IMonitorService monitorService, IPowerService powerService)
    {
        ScanError = data.ScanError;
        var panel = new InternalPanelService(powerService);
        foreach (var device in data.Monitors)
            Monitors.Add(new MonitorItemViewModel(device, monitorService, panel));
    }
}

public sealed partial class MonitorItemViewModel : ViewModelBase
{
    private readonly MonitorDevice _device;
    private readonly IMonitorService _monitors;
    private readonly InternalPanelService _panel;

    // Latest-wins write coalescing: a moving slider produces values faster
    // than DDC accepts them; one write runs at a time and only the newest
    // queued value survives.
    private int _brightnessWriting;
    private int? _brightnessPending;
    private int _contrastWriting;
    private int? _contrastPending;

    public MonitorItemViewModel(MonitorDevice device, IMonitorService monitors, InternalPanelService panel)
    {
        _device = device;
        _monitors = monitors;
        _panel = panel;
        _brightness = device.Brightness;
        _contrast = device.Contrast ?? 0;
        _selectedInput = device.InputSources.FirstOrDefault(i => i.Value == device.CurrentInput);
    }

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

    partial void OnBrightnessChanged(double value) =>
        _ = PushAsync((int)value, brightness: true);

    partial void OnContrastChanged(double value) =>
        _ = PushAsync((int)value, brightness: false);

    partial void OnLastErrorChanged(string value) => OnPropertyChanged(nameof(HasError));

    [RelayCommand]
    private async Task SetInputAsync()
    {
        if (SelectedInput is not { } input)
            return;

        var result = await Task.Run(() => _monitors.SetInputSource(_device.Id, input.Value)).ConfigureAwait(true);
        LastError = result.IsSuccess ? string.Empty : result.ErrorMessage ?? "Input switch failed.";
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
