using CommunityToolkit.Mvvm.ComponentModel;
using ThisIsMyPC.Core.Services;
using ThisIsMyPC.Modules.Startup.Models;

namespace ThisIsMyPC.App.ViewModels;

/// <summary>
/// Startup &amp; Services: the Autoruns inventory as Autoruns lays it out, one
/// tab per category plus Everything, with a text filter and a Microsoft
/// filter shared by every tab. Each row stages its own enable or disable
/// through the pending pipeline.
/// </summary>
public sealed partial class StartupViewModel : ObservableObject, IDisposable
{
    private readonly List<AutorunItemViewModel> _allAutoruns = [];

    [ObservableProperty]
    private string _autorunFilterText = string.Empty;

    [ObservableProperty]
    private bool _hideMicrosoftAutoruns;

    public StartupViewModel(StartupScanData scanData, IPendingChangesService pendingChangesService)
    {
        ArgumentNullException.ThrowIfNull(scanData);
        _allAutoruns.AddRange(scanData.Autoruns.Select(a => new AutorunItemViewModel(a, pendingChangesService)));
        AutorunsScanError = scanData.AutorunsScanError ?? scanData.ScheduledTasksScanError ?? scanData.ServicesScanError;
        Tabs = [new AutorunTabViewModel(null), .. Enum.GetValues<AutorunCategory>().Select(c => new AutorunTabViewModel(c))];
        RebuildAutoruns();
    }

    public string? AutorunsScanError { get; }

    /// <summary>Everything first, then the categories in Autoruns' order.</summary>
    public IReadOnlyList<AutorunTabViewModel> Tabs { get; }

    partial void OnAutorunFilterTextChanged(string value) => RebuildAutoruns();
    partial void OnHideMicrosoftAutorunsChanged(bool value) => RebuildAutoruns();

    private void RebuildAutoruns()
    {
        var filter = AutorunFilterText.Trim();
        var byCategory = new Dictionary<AutorunCategory, (List<AutorunItemViewModel> Visible, int Total)>();
        foreach (var category in Enum.GetValues<AutorunCategory>())
        {
            var all = _allAutoruns.Where(a => a.Entry.Category == category).ToList();
            var visible = all
                .Where(a => !HideMicrosoftAutoruns || !a.IsMicrosoft)
                .Where(a => filter.Length == 0 || a.Matches(filter))
                .OrderBy(a => a.Name, StringComparer.OrdinalIgnoreCase)
                .ToList();
            byCategory[category] = (visible, all.Count);
        }

        foreach (var tab in Tabs)
        {
            if (tab.Category is { } category)
            {
                var (visible, total) = byCategory[category];
                var groups = visible.Count == 0 ? [] : new[] { new AutorunGroupViewModel(category, visible, total, showHeader: false) };
                tab.Replace(groups, visible.Count, total);
            }
            else
            {
                var groups = Enum.GetValues<AutorunCategory>()
                    .Where(c => byCategory[c].Visible.Count > 0)
                    .Select(c => new AutorunGroupViewModel(c, byCategory[c].Visible, byCategory[c].Total, showHeader: true))
                    .ToList();
                tab.Replace(groups, byCategory.Sum(p => p.Value.Visible.Count), _allAutoruns.Count);
            }
        }
    }

    public void Dispose()
    {
        foreach (var item in _allAutoruns)
            item.Dispose();
    }
}
