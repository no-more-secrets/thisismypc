using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using ThisIsMyPC.Core.Services;
using ThisIsMyPC.Modules.Startup.Models;

namespace ThisIsMyPC.App.ViewModels;

/// <summary>
/// Startup &amp; Services: the Autoruns inventory, grouped by Autoruns
/// category, with a text filter, a category picker, and a Microsoft filter.
/// Each row stages its own enable or disable through the pending pipeline.
/// </summary>
public sealed partial class StartupViewModel : ObservableObject, IDisposable
{
    private readonly List<AutorunItemViewModel> _allAutoruns = [];

    [ObservableProperty]
    private string _autorunFilterText = string.Empty;

    [ObservableProperty]
    private string _autorunCategoryFilter = "All";

    [ObservableProperty]
    private bool _hideMicrosoftAutoruns;

    public static IReadOnlyList<string> AutorunCategoryFilterOptions { get; } =
        ["All", .. Enum.GetValues<AutorunCategory>().Select(AutorunEntry.CategoryName)];

    public StartupViewModel(StartupScanData scanData, IPendingChangesService pendingChangesService)
    {
        ArgumentNullException.ThrowIfNull(scanData);
        _allAutoruns.AddRange(scanData.Autoruns.Select(a => new AutorunItemViewModel(a, pendingChangesService)));
        AutorunsScanError = scanData.AutorunsScanError ?? scanData.ScheduledTasksScanError ?? scanData.ServicesScanError;
        AutorunGroups = [];
        RebuildAutoruns();
    }

    public string? AutorunsScanError { get; }
    public ObservableCollection<AutorunGroupViewModel> AutorunGroups { get; }
    public string AutorunsHeader => $"Autoruns ({AutorunGroups.Sum(g => g.Items.Count)} of {_allAutoruns.Count})";
    public bool HasVisibleAutoruns => AutorunGroups.Count > 0;

    partial void OnAutorunFilterTextChanged(string value) => RebuildAutoruns();
    partial void OnAutorunCategoryFilterChanged(string value) => RebuildAutoruns();
    partial void OnHideMicrosoftAutorunsChanged(bool value) => RebuildAutoruns();

    /// <summary>Groups in Autoruns' tab order; a category with nothing visible is left out.</summary>
    private void RebuildAutoruns()
    {
        var filter = AutorunFilterText.Trim();
        AutorunGroups.Clear();
        foreach (var category in Enum.GetValues<AutorunCategory>())
        {
            var name = AutorunEntry.CategoryName(category);
            if (AutorunCategoryFilter != "All" && AutorunCategoryFilter != name)
                continue;

            var all = _allAutoruns.Where(a => a.Entry.Category == category).ToList();
            var visible = all
                .Where(a => !HideMicrosoftAutoruns || !a.IsMicrosoft)
                .Where(a => filter.Length == 0 || a.Matches(filter))
                .OrderBy(a => a.Name, StringComparer.OrdinalIgnoreCase)
                .ToList();
            if (visible.Count > 0)
                AutorunGroups.Add(new AutorunGroupViewModel(category, visible, all.Count));
        }
        OnPropertyChanged(nameof(AutorunsHeader));
        OnPropertyChanged(nameof(HasVisibleAutoruns));
    }

    public void Dispose()
    {
        foreach (var item in _allAutoruns)
            item.Dispose();
    }
}
