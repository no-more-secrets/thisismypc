using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;

namespace ThisIsMyPC.App.ViewModels;

/// <summary>
/// One run of ExplorerPatcher rows on an Explorer tab, under a sub-heading
/// named after ExplorerPatcher's own settings page (System tray, Window
/// switcher, Weather widget). Rows keep the manifest's order, toggles and
/// choices interleaved, so the page reads the way ExplorerPatcher's does. The
/// tab's main run has no heading and comes first.
/// </summary>
public sealed partial class ExplorerPatcherGroupViewModel : ViewModelBase
{
    public string Heading { get; }

    public bool ShowHeading => Heading.Length > 0;

    /// <summary>ShellSettingViewModel and ShellChoiceSettingViewModel, in manifest order.</summary>
    public ObservableCollection<ViewModelBase> Rows { get; } = [];

    /// <summary>False while the search hides every row, so the heading goes too.</summary>
    [ObservableProperty]
    private bool _isSearchVisible = true;

    public ExplorerPatcherGroupViewModel(string heading)
    {
        Heading = heading;
    }

    public IEnumerable<ShellSettingViewModel> Toggles => Rows.OfType<ShellSettingViewModel>();

    public IEnumerable<ShellChoiceSettingViewModel> Choices => Rows.OfType<ShellChoiceSettingViewModel>();

    public void ApplySearch(string query)
    {
        var any = false;
        foreach (var row in Rows)
        {
            switch (row)
            {
                case ShellSettingViewModel toggle:
                    toggle.ApplySearch(query);
                    any |= toggle.IsSearchVisible;
                    break;
                case ShellChoiceSettingViewModel choice:
                    choice.ApplySearch(query);
                    any |= choice.IsSearchVisible;
                    break;
            }
        }
        IsSearchVisible = any;
    }
}
