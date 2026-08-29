using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.Input;
using ThisIsMyPC.Core.Monitoring;

namespace ThisIsMyPC.App.ViewModels;

/// <summary>One unreviewed monitoring detection row (9-3).</summary>
public sealed partial class DetectionRowViewModel : ViewModelBase
{
    private readonly Action<DetectionRowViewModel> _disable;
    private readonly Action<DetectionRowViewModel> _dismiss;

    public DetectionRowViewModel(
        MonitoringDetection detection,
        Action<DetectionRowViewModel> disable,
        Action<DetectionRowViewModel> dismiss)
    {
        Detection = detection;
        _disable = disable;
        _dismiss = dismiss;
    }

    public MonitoringDetection Detection { get; }
    public string DisplayName => Detection.DisplayName;
    public string Detail => $"{Detection.Source} - detected {Detection.DetectedAt.ToLocalTime():g}";

    [RelayCommand]
    private void Disable() => _disable(this);

    [RelayCommand]
    private void Dismiss() => _dismiss(this);
}

/// <summary>The Home "new since last review" section (9-3).</summary>
public sealed class MonitoringSectionViewModel
{
    public MonitoringSectionViewModel(IReadOnlyList<DetectionRowViewModel> rows)
    {
        foreach (var row in rows)
            Rows.Add(row);
    }

    public ObservableCollection<DetectionRowViewModel> Rows { get; } = [];

    public bool HasRows => Rows.Count > 0;

    public string Header => $"New startup items since last review ({Rows.Count})";
}
