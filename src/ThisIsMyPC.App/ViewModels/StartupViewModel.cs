using CommunityToolkit.Mvvm.ComponentModel;
using ThisIsMyPC.Core.Services;
using ThisIsMyPC.Modules.Startup.Models;

namespace ThisIsMyPC.App.ViewModels;

/// <summary>
/// Startup &amp; Services: the Autoruns inventory as Autoruns lays it out, one
/// tab per category. Typing in the filter box replaces the tabs with one
/// list across every category, grouped under category headers; clearing it
/// brings the tabs back. "Hide Microsoft entries" applies to both.
/// </summary>
public sealed partial class StartupViewModel : ObservableObject, IDisposable
{
    private readonly List<AutorunItemViewModel> _allAutoruns = [];

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsSearching))]
    private string _autorunFilterText = string.Empty;

    [ObservableProperty]
    private bool _hideMicrosoftAutoruns;

    /// <summary>Header rows (AutorunSearchHeader) and item rows, in category order, while searching.</summary>
    [ObservableProperty]
    private IReadOnlyList<object> _searchResults = [];

    [ObservableProperty]
    private string _searchSummary = string.Empty;

    public StartupViewModel(StartupScanData scanData, IPendingChangesService pendingChangesService)
    {
        ArgumentNullException.ThrowIfNull(scanData);
        _allAutoruns.AddRange(scanData.Autoruns.Select(a => new AutorunItemViewModel(a, pendingChangesService)));
        AutorunsScanError = scanData.AutorunsScanError ?? scanData.ScheduledTasksScanError ?? scanData.ServicesScanError;
        Tabs = [.. Enum.GetValues<AutorunCategory>().Select(c => new AutorunTabViewModel(c))];
        RebuildTabs();
    }

    public string? AutorunsScanError { get; }

    /// <summary>The categories in Autoruns' order.</summary>
    public IReadOnlyList<AutorunTabViewModel> Tabs { get; }

    public bool IsSearching => AutorunFilterText.Trim().Length > 0;

    partial void OnAutorunFilterTextChanged(string value) => RebuildSearch();

    partial void OnHideMicrosoftAutorunsChanged(bool value)
    {
        RebuildTabs();
        RebuildSearch();
    }

    private IEnumerable<AutorunItemViewModel> Visible(AutorunCategory category)
        => _allAutoruns
            .Where(a => a.Entry.Category == category)
            .Where(a => !HideMicrosoftAutoruns || !a.IsMicrosoft)
            .OrderBy(a => a.Name, StringComparer.OrdinalIgnoreCase);

    private void RebuildTabs()
    {
        foreach (var tab in Tabs)
        {
            var total = _allAutoruns.Count(a => a.Entry.Category == tab.Category);
            tab.Replace(Visible(tab.Category).ToList(), total);
        }
    }

    private void RebuildSearch()
    {
        var filter = AutorunFilterText.Trim();
        if (filter.Length == 0)
        {
            SearchResults = [];
            SearchSummary = string.Empty;
            return;
        }

        var rows = new List<object>();
        var matches = 0;
        foreach (var category in Enum.GetValues<AutorunCategory>())
        {
            var hits = Visible(category).Where(a => a.Matches(filter)).ToList();
            if (hits.Count == 0)
                continue;
            rows.Add(new AutorunSearchHeader($"{AutorunEntry.CategoryName(category)} ({hits.Count})"));
            rows.AddRange(hits);
            matches += hits.Count;
        }
        SearchResults = rows;
        SearchSummary = matches switch
        {
            0 => "Nothing matches",
            1 => "1 match across every category",
            _ => $"{matches} matches across every category",
        };
    }

    public void Dispose()
    {
        foreach (var item in _allAutoruns)
            item.Dispose();
    }
}
