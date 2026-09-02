using System.Collections.ObjectModel;
using System.Globalization;
using Avalonia.Media;
using CommunityToolkit.Mvvm.Input;
using ThisIsMyPC.Core.Services;

namespace ThisIsMyPC.App.ViewModels;

/// <summary>One Quick Actions entry: an available module the user can jump to.</summary>
public sealed partial class QuickActionViewModel : ViewModelBase
{
    private readonly Action _navigate;
    private readonly Func<Geometry> _iconGeometryFactory;
    private Geometry? _cachedGeometry;

    public QuickActionViewModel(string name, Func<Geometry> iconGeometryFactory, Action navigate)
    {
        Name = name;
        _iconGeometryFactory = iconGeometryFactory;
        _navigate = navigate;
    }

    public string Name { get; }

    /// <summary>
    /// Geometry resolved through the sidebar item's icon-key mapping (raw
    /// Module.Info.Icon is a KEY like "shell", not path markup). Resolved lazily:
    /// Geometry parsing needs a rendering platform, which headless tests lack;
    /// only real rendering may evaluate this.
    /// </summary>
    public Geometry IconGeometry => _cachedGeometry ??= _iconGeometryFactory();

    [RelayCommand]
    private void Open() => _navigate();
}

/// <summary>One Recent Activity row: an applied change batch from history.</summary>
public sealed class RecentActivityItemViewModel
{
    public required string DisplayName { get; init; }
    public required string AppliedAtDisplay { get; init; }
    public required bool IsReverted { get; init; }
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
    public IReadOnlyList<QuickActionViewModel> QuickActions { get; }
    public ObservableCollection<RecentActivityItemViewModel> RecentActivity { get; } = [];

    public bool HasRecentActivity => RecentActivity.Count > 0;

    /// <summary>First-launch capability summary (5-2); null after dismissal.</summary>
    public FirstLaunchBannerViewModel? FirstLaunchBanner { get; }

    /// <summary>Unreviewed monitoring detections (9-3); null when none or monitoring off.</summary>
    public MonitoringSectionViewModel? MonitoringSection { get; }

    public HomeViewModel(
        SystemIdentity identity,
        IReadOnlyList<QuickActionViewModel> quickActions,
        IChangeHistoryService historyService,
        FirstLaunchBannerViewModel? firstLaunchBanner = null,
        MonitoringSectionViewModel? monitoringSection = null,
        DriftSectionViewModel? driftSection = null)
    {
        Identity = identity;
        QuickActions = quickActions;
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

            RecentActivity.Clear();
            var batches = entries
                .GroupBy(e => e.GroupId ?? e.Id.ToString(CultureInfo.InvariantCulture))
                .Take(RecentActivityLimit);

            foreach (var batch in batches)
            {
                var primary = batch.First();
                var names = batch.Select(e => e.DisplayName).Distinct().ToList();
                RecentActivity.Add(new RecentActivityItemViewModel
                {
                    DisplayName = names.Count == 1 ? names[0] : string.Join(", ", names),
                    AppliedAtDisplay = primary.AppliedAt.LocalDateTime
                        .ToString("MMM d, HH:mm", CultureInfo.CurrentCulture),
                    IsReverted = batch.All(e => e.RevertedAt.HasValue),
                });
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
}
