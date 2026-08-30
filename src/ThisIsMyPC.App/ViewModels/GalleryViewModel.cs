using CommunityToolkit.Mvvm.ComponentModel;

namespace ThisIsMyPC.App.ViewModels;

/// <summary>
/// The UI Gallery: every standardized style, class, and token on one page so
/// design changes can be judged in one look. Dev-facing; hide before release.
/// </summary>
public sealed partial class GalleryViewModel : ViewModelBase
{
    [ObservableProperty]
    private bool _sampleToggleOn = true;

    [ObservableProperty]
    private bool _sampleChipChecked = true;

    [ObservableProperty]
    private string _sampleText = string.Empty;

    public IReadOnlyList<string> SampleOptions { get; } = ["First option", "Second option", "Third option"];

    [ObservableProperty]
    private string _selectedOption = "First option";
}
