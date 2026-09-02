using CommunityToolkit.Mvvm.ComponentModel;
using ThisIsMyPC.App.Services;
using ThisIsMyPC.Core.Services;
using ThisIsMyPC.Modules.Startup.Models;

namespace ThisIsMyPC.App.ViewModels;

/// <summary>
/// Startup &amp; Services: the Autoruns inventory as Autoruns lays it out, one
/// tab per category, rows grouped under their location with the key's or
/// folder's write time. Typing in the filter box replaces the tabs with one
/// list across every category. Windows and Microsoft entries stay hidden
/// until their boxes are ticked; both filters apply to tabs and search. Icons and signers load in the
/// background after the page is up; the Windows filter is re-applied once
/// they are known.
/// </summary>
public sealed partial class StartupViewModel : ObservableObject, IDisposable
{
    private readonly List<AutorunItemViewModel> _allAutoruns = [];
    private readonly CancellationTokenSource _enrichment = new();

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsSearching))]
    private string _autorunFilterText = string.Empty;

    /// <summary>Windows' own entries (signed by Microsoft Windows) show only when ticked.</summary>
    [ObservableProperty]
    private bool _showWindowsEntries;

    /// <summary>Microsoft-published entries that are not part of Windows (Office, OneDrive, Teams) show only when ticked.</summary>
    [ObservableProperty]
    private bool _showMicrosoftEntries;

    /// <summary>Rows show their file path only when asked; the list stays one line per row otherwise.</summary>
    [ObservableProperty]
    private bool _showPaths;

    /// <summary>Location headers and each row's key or folder show only when asked; the view binds it, nothing rebuilds.</summary>
    [ObservableProperty]
    private bool _showLocations;

    /// <summary>Header rows (category, location) and item rows, in category order, while searching.</summary>
    [ObservableProperty]
    private IReadOnlyList<object> _searchResults = [];

    [ObservableProperty]
    private string _searchSummary = string.Empty;

    [ObservableProperty]
    private bool _isCheckingSignatures;

    public StartupViewModel(StartupScanData scanData, IPendingChangesService pendingChangesService, AutorunEnrichment? enrichment = null)
    {
        ArgumentNullException.ThrowIfNull(scanData);
        _allAutoruns.AddRange(scanData.Autoruns.Select(a => new AutorunItemViewModel(a, pendingChangesService)));
        AutorunsScanError = scanData.AutorunsScanError ?? scanData.ScheduledTasksScanError ?? scanData.ServicesScanError;
        Tabs = [.. Enum.GetValues<AutorunCategory>().Select(c => new AutorunTabViewModel(c))];
        RebuildTabs();

        if (enrichment is not null && (enrichment.HasIcons || enrichment.HasSignatures))
            _ = EnrichAllAsync(enrichment);
    }

    public string? AutorunsScanError { get; }

    /// <summary>The categories in Autoruns' order.</summary>
    public IReadOnlyList<AutorunTabViewModel> Tabs { get; }

    public bool IsSearching => AutorunFilterText.Trim().Length > 0;

    partial void OnAutorunFilterTextChanged(string value) => RebuildSearch();

    partial void OnShowWindowsEntriesChanged(bool value)
    {
        RebuildTabs();
        RebuildSearch();
    }

    partial void OnShowMicrosoftEntriesChanged(bool value)
    {
        RebuildTabs();
        RebuildSearch();
    }

    private IEnumerable<AutorunItemViewModel> Visible(AutorunCategory category)
        => _allAutoruns
            .Where(a => a.Entry.Category == category)
            // Two disjoint groups: a Windows entry answers to the Windows box
            // alone, a non-Windows Microsoft entry to the Microsoft box alone.
            .Where(a => a.IsWindowsEntry ? ShowWindowsEntries : ShowMicrosoftEntries || !a.IsMicrosoft);

    /// <summary>Rows grouped under their location, locations in first-seen (catalog) order, rows by name. The view shows or hides the headers.</summary>
    private static List<object> WithLocationHeaders(IEnumerable<AutorunItemViewModel> rows)
    {
        var result = new List<object>();
        foreach (var group in rows.GroupBy(r => r.Entry.LocationGroup, StringComparer.OrdinalIgnoreCase))
        {
            result.Add(new AutorunLocationHeader(group.Key, group.First().Entry.LocationTimestamp));
            result.AddRange(group.OrderBy(r => r.Name, StringComparer.OrdinalIgnoreCase));
        }
        return result;
    }

    private void RebuildTabs()
    {
        foreach (var tab in Tabs)
        {
            var visible = Visible(tab.Category).ToList();
            tab.Replace(WithLocationHeaders(visible), visible.Count);
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
            rows.AddRange(WithLocationHeaders(hits));
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

    /// <summary>Icons and signers for every row, then one rebuild so the Windows filter sees the signers.</summary>
    private async Task EnrichAllAsync(AutorunEnrichment enrichment)
    {
        IsCheckingSignatures = enrichment.HasSignatures;
        try
        {
            var token = _enrichment.Token;
            foreach (var row in _allAutoruns)
            {
                if (token.IsCancellationRequested)
                    return;
                try
                {
                    await row.EnrichAsync(enrichment, token).ConfigureAwait(true);
                }
                catch (OperationCanceledException)
                {
                    return;
                }
            }
            RebuildTabs();
            RebuildSearch();
        }
        finally
        {
            IsCheckingSignatures = false;
        }
    }

    public void Dispose()
    {
        _enrichment.Cancel();
        _enrichment.Dispose();
        foreach (var item in _allAutoruns)
            item.Dispose();
    }
}
