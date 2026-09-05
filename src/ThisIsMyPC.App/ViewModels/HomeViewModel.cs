using System.Collections.ObjectModel;
using System.Globalization;
using CommunityToolkit.Mvvm.Input;
using ThisIsMyPC.Core.Services;

namespace ThisIsMyPC.App.ViewModels;

/// <summary>One recent activity card built from an applied change batch.</summary>
public sealed class RecentActivityItemViewModel
{
    public required string DisplayName { get; init; }
    public required string AppliedAtDisplay { get; init; }
    public required string ModuleDisplay { get; init; }
    public required string OperationCountDisplay { get; init; }
    public required bool IsReverted { get; init; }
    public required IReadOnlyList<RecentActivityDetailViewModel> Details { get; init; }
    public string StatusDisplay => IsReverted ? "Restored" : "Applied";
}

/// <summary>One setting transition inside a recent activity batch.</summary>
public sealed class RecentActivityDetailViewModel
{
    public required string DisplayName { get; init; }
    public required string ChangeDisplay { get; init; }
}

/// <summary>Recent activity cards applied on one calendar day.</summary>
public sealed class RecentActivityGroupViewModel
{
    public required string DateHeader { get; init; }
    public ObservableCollection<RecentActivityItemViewModel> Items { get; } = [];
}

/// <summary>
/// Home tab (10.5): read-only dashboard and navigation surface. Not a module; no
/// settings, no pending changes, no display modes. Renders from cheap/cached data;
/// recent activity loads async after construction without blocking.
/// </summary>
public sealed partial class HomeViewModel : ViewModelBase
{
    private static readonly NLog.Logger Log = NLog.LogManager.GetLogger("ThisIsMyPC.App.ViewModels.HomeViewModel");

    private const int RecentActivityLimit = 5;

    private readonly IChangeHistoryService _historyService;

    public SystemIdentity Identity { get; }
    public ObservableCollection<RecentActivityGroupViewModel> RecentActivityGroups { get; } = [];

    public bool HasRecentActivity => RecentActivityGroups.Count > 0;

    /// <summary>First-launch capability summary (5-2); null after dismissal.</summary>
    public FirstLaunchBannerViewModel? FirstLaunchBanner { get; }

    /// <summary>Unreviewed monitoring detections (9-3); null when none or monitoring off.</summary>
    public MonitoringSectionViewModel? MonitoringSection { get; }

    public HomeViewModel(
        SystemIdentity identity,
        IChangeHistoryService historyService,
        FirstLaunchBannerViewModel? firstLaunchBanner = null,
        MonitoringSectionViewModel? monitoringSection = null,
        DriftSectionViewModel? driftSection = null)
    {
        Identity = identity;
        _historyService = historyService;
        FirstLaunchBanner = firstLaunchBanner;
        MonitoringSection = monitoringSection;
        DriftSection = driftSection;
    }

    /// <summary>Owner Mode drift report (28-3); null when the service found nothing (or is off).</summary>
    public DriftSectionViewModel? DriftSection { get; }

    [RelayCommand]
    public async Task LoadRecentActivityAsync()
    {
        try
        {
            var entries = await _historyService.GetRecentGroupedAsync(RecentActivityLimit)
                .ConfigureAwait(true);

            RecentActivityGroups.Clear();
            var batches = entries
                .GroupBy(e => e.GroupId ?? e.Id.ToString(CultureInfo.InvariantCulture))
                .Take(RecentActivityLimit);

            foreach (var dateGroup in batches.GroupBy(batch => DateHeader(batch.First().AppliedAt)))
            {
                var group = new RecentActivityGroupViewModel { DateHeader = dateGroup.Key };
                foreach (var batch in dateGroup)
                {
                    var primary = batch.First();
                    var names = batch.Select(e => e.DisplayName).Distinct().ToList();
                    group.Items.Add(new RecentActivityItemViewModel
                    {
                        DisplayName = names.Count == 1 ? names[0] : string.Join(", ", names),
                        AppliedAtDisplay = primary.AppliedAt.LocalDateTime.ToString("h:mm tt", CultureInfo.CurrentCulture),
                        ModuleDisplay = FormatModule(primary.ModuleId),
                        OperationCountDisplay = batch.Count() == 1 ? "1 operation" : $"{batch.Count()} operations",
                        IsReverted = batch.All(e => e.RevertedAt.HasValue),
                        Details = batch.Take(3).Select(entry => new RecentActivityDetailViewModel
                        {
                            DisplayName = entry.DisplayName,
                            ChangeDisplay = FormatChange(entry),
                        }).ToList(),
                    });
                }

                RecentActivityGroups.Add(group);
            }

        }
        catch (Exception ex)
        {
            // The dashboard never blocks or breaks on history problems.
            Log.Warn(ex, "Home recent activity load failed");
        }
        finally
        {
            OnPropertyChanged(nameof(HasRecentActivity));
        }
    }

    private static string DateHeader(DateTimeOffset appliedAt)
    {
        var date = appliedAt.LocalDateTime.Date;
        var today = DateTime.Today;
        if (date == today) return "Today";
        if (date == today.AddDays(-1)) return "Yesterday";
        return date.ToString("MMMM d, yyyy", CultureInfo.CurrentCulture);
    }

    private static string FormatModule(string moduleId)
        => string.IsNullOrWhiteSpace(moduleId) ? "System" : moduleId.Replace('_', ' ');

    private static string FormatChange(Core.Changes.ChangeHistoryEntry entry)
    {
        var before = entry.BeforeDisplay ?? entry.BeforeValue;
        var after = entry.AfterDisplay ?? entry.AfterValue;
        return !string.IsNullOrWhiteSpace(before) && !string.IsNullOrWhiteSpace(after)
            ? $"{before} to {after}"
            : entry.Category.ToString();
    }
}
