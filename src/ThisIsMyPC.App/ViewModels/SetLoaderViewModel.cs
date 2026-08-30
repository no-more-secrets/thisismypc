using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ThisIsMyPC.Core.Modules;
using ThisIsMyPC.Core.Services;
using ThisIsMyPC.Core.Sets;

namespace ThisIsMyPC.App.ViewModels;

/// <summary>
/// The Set Loader screen: browse loaded sets by category (8.2), preview every change
/// with its live current value, resolve conflicts, and stage the included entries into
/// the standard pending-changes pipeline (8.3).
/// </summary>
public partial class SetLoaderViewModel : ViewModelBase, IDisposable
{
    private readonly IReadOnlyList<ISetEntryInspector> _inspectors;
    private readonly SetConflictResolver _conflictResolver;
    private readonly IPendingChangesService _pendingChangesService;
    private bool _suppressPendingRefresh;

    public ObservableCollection<SetItemViewModel> TweakSets { get; } = [];
    public ObservableCollection<SetItemViewModel> OptimizationPacks { get; } = [];
    public ObservableCollection<SetPreviewGroupViewModel> PreviewGroups { get; } = [];

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasNoSelection))]
    private SetItemViewModel? _selectedSet;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasIncludedEntries))]
    [NotifyPropertyChangedFor(nameof(StageButtonText))]
    private int _includedCount;

    [ObservableProperty]
    private string _stageMessage = string.Empty;

    public bool HasTweakSets => TweakSets.Count > 0;
    public bool HasOptimizationPacks => OptimizationPacks.Count > 0;
    public bool HasNoSets => TweakSets.Count == 0 && OptimizationPacks.Count == 0;
    public bool HasNoSelection => SelectedSet is null;
    public bool HasIncludedEntries => IncludedCount > 0;
    public string StageButtonText => IncludedCount == 1
        ? "Stage 1 change"
        : $"Stage {IncludedCount} changes";

    public SetLoaderViewModel(
        SetLoadResult loadResult,
        IEnumerable<ISetEntryInspector> inspectors,
        Func<string, ModuleAvailability?> moduleAvailabilityLookup,
        IPendingChangesService pendingChangesService,
        ICapabilityDetector? capabilityDetector = null)
    {
        _inspectors = inspectors.ToList();
        _conflictResolver = new SetConflictResolver(_inspectors, moduleAvailabilityLookup, capabilityDetector);
        _pendingChangesService = pendingChangesService;

        foreach (var set in loadResult.Sets.OrderBy(s => s.Name, StringComparer.OrdinalIgnoreCase))
        {
            var item = new SetItemViewModel(set);
            (set.Category == SetCategory.OptimizationPack ? OptimizationPacks : TweakSets).Add(item);
        }

        // The review panel (Discard All / Apply) is reachable while this screen is open;
        // re-resolve so "already staged" tags and conflicts never go stale.
        _pendingChangesService.PropertyChanged += OnPendingChangesChanged;
    }

    public void Dispose() => _pendingChangesService.PropertyChanged -= OnPendingChangesChanged;

    private void OnPendingChangesChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(IPendingChangesService.PendingCount) && !_suppressPendingRefresh)
            BuildPreview();
    }

    [RelayCommand]
    private void SelectSet(SetItemViewModel? item)
    {
        foreach (var set in TweakSets.Concat(OptimizationPacks))
            set.IsSelected = ReferenceEquals(set, item);

        SelectedSet = item;
        StageMessage = string.Empty;
        BuildPreview();
    }

    [RelayCommand]
    private void StageIncluded()
    {
        var staged = 0;
        // Each Stage/Unstage raises PendingCount; hold the rebuild until the loop is done
        // (BuildPreview would clear the collection being iterated).
        _suppressPendingRefresh = true;
        try
        {
            foreach (var row in PreviewGroups.SelectMany(g => g.Entries))
            {
                if (!row.IsIncluded || row.IsSkipped)
                    continue;

                // Fresh build at stage time: before-values must reflect the system NOW,
                // not the moment the preview was rendered.
                var group = _inspectors
                    .FirstOrDefault(i => i.ModuleId == row.Entry.ModuleId)?
                    .CreateChangeGroup(row.Entry);
                if (group is null)
                    continue;

                // Any conflicting pending group is replaced; including the same-value
                // case, where staging on top would duplicate the mutation.
                if (row.Resolution.Conflict
                        is SetEntryConflict.PendingDifferentValue or SetEntryConflict.PendingSameValue
                    && row.Resolution.PendingGroupId is not null)
                {
                    _pendingChangesService.Unstage(row.Resolution.PendingGroupId);
                }

                _pendingChangesService.Stage(group);
                staged++;
            }
        }
        finally
        {
            _suppressPendingRefresh = false;
        }

        StageMessage = staged == 1
            ? "1 change staged. Review and apply from the pending changes bar."
            : $"{staged} changes staged. Review and apply from the pending changes bar.";

        // Re-resolve: freshly staged rows flip to "already staged" and uncheck.
        BuildPreview();
    }

    private void BuildPreview()
    {
        PreviewGroups.Clear();
        if (SelectedSet is null)
        {
            RecountIncluded();
            return;
        }

        var definition = SelectedSet.Definition;
        // Positional pairing, NOT a dictionary: SetEntry is a value-equality record and
        // duplicate rows in a hand-authored user set must not crash the preview.
        var resolutions = _conflictResolver.Resolve(definition, _pendingChangesService.PendingGroups);
        var pairs = definition.Entries.Zip(resolutions);

        var grouped = definition.Category == SetCategory.OptimizationPack
            ? pairs.GroupBy(p => p.First.Group ?? "Other")
            : pairs.GroupBy(_ => string.Empty);

        foreach (var group in grouped)
        {
            var groupVm = new SetPreviewGroupViewModel(group.Key);
            foreach (var (_, resolution) in group)
                groupVm.Entries.Add(new SetEntryPreviewViewModel(resolution, RecountIncluded));
            PreviewGroups.Add(groupVm);
        }

        RecountIncluded();
    }

    private void RecountIncluded()
        => IncludedCount = PreviewGroups
            .SelectMany(g => g.Entries)
            .Count(e => e.IsIncluded && !e.IsSkipped);
}
