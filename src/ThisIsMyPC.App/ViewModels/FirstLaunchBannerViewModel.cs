using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace ThisIsMyPC.App.ViewModels;

/// <summary>One line of the first-launch summary.</summary>
public sealed partial class FirstLaunchRowViewModel : ViewModelBase
{
    private readonly Action? _navigate;

    public FirstLaunchRowViewModel(string title, string detail, bool isAvailable, Action? navigate = null)
    {
        Title = title;
        Detail = detail;
        IsAvailable = isAvailable;
        _navigate = navigate;
    }

    public string Title { get; }
    public string Detail { get; }
    public bool IsAvailable { get; }
    public bool CanNavigate => _navigate is not null;

    [RelayCommand]
    private void Navigate() => _navigate?.Invoke();
}

/// <summary>
/// Dismissible first-launch capability summary (5-2), rendered at the top of Home.
/// Dismissal is persisted by the host (settings), so this VM only raises the event.
/// </summary>
public sealed partial class FirstLaunchBannerViewModel : ViewModelBase
{
    public FirstLaunchBannerViewModel(
        IReadOnlyList<FirstLaunchRowViewModel> moduleRows,
        IReadOnlyList<FirstLaunchRowViewModel> capabilityRows)
    {
        ModuleRows = moduleRows;
        CapabilityRows = capabilityRows;
    }

    public IReadOnlyList<FirstLaunchRowViewModel> ModuleRows { get; }
    public IReadOnlyList<FirstLaunchRowViewModel> CapabilityRows { get; }

    [ObservableProperty]
    private bool _isVisible = true;

    public event EventHandler? Dismissed;

    [RelayCommand]
    private void Dismiss()
    {
        IsVisible = false;
        Dismissed?.Invoke(this, EventArgs.Empty);
    }
}
