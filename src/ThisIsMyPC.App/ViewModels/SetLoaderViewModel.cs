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
public partial class SetLoaderViewModel : ViewModelBase, IDisposable, ITabbedPage
{
    private readonly IReadOnlyList<ISetEntryInspector> _inspectors;
    private readonly SetConflictResolver _conflictResolver;
    private readonly IPendingChangesService _pendingChangesService;
    private bool _suppressPendingRefresh;
    private readonly Action<string, string, string>? _navigateToSetting;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CurrentSets))]
    [NotifyPropertyChangedFor(nameof(HasNoCategorySets))]
    private int _selectedTabIndex;

    [ObservableProperty]
    private string _searchText = string.Empty;

    public IReadOnlyList<object> VisibleRows { get; private set; } = [];
    public bool HasNoSearchResults => !HasNoSelection && VisibleRows.Count == 0;
    public ObservableCollection<SetItemViewModel> CurrentSets => SelectedTabIndex == 1 ? OptimizationPacks : TweakSets;
    public bool HasNoCategorySets => CurrentSets.Count == 0;

    partial void OnSelectedTabIndexChanged(int value) => SelectSet(null);
    partial void OnSearchTextChanged(string value) => FilterPreview();

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
    public bool HasIncludedEntries => IncludedCount > 0 && CanStagePack;
    public string StageButtonText => AppRemovals.Any(r => r.IsIncluded && r.CanInclude)
        ? $"Stage {IncludedCount} selection{(IncludedCount == 1 ? "" : "s")}"
        : IncludedCount == 1
        ? "Stage 1 change"
        : $"Stage {IncludedCount} changes";

    public SetLoaderViewModel(
        SetLoadResult loadResult,
        IEnumerable<ISetEntryInspector> inspectors,
        Func<string, ModuleAvailability?> moduleAvailabilityLookup,
        IPendingChangesService pendingChangesService,
        ICapabilityDetector? capabilityDetector = null,
        Action<string, string, string>? navigateToSetting = null,
        Core.Policies.PolicyControlStateReader? policyStates = null,
        Core.Hardware.IHardwareDetectionService? hardwareDetection = null,
        Core.Packages.IAppxPackageService? appxPackages = null,
        IPendingActionsService? pendingActions = null)
    {
        _inspectors = inspectors.ToList();
        _conflictResolver = new SetConflictResolver(_inspectors, moduleAvailabilityLookup, capabilityDetector, policyStates);
        _pendingChangesService = pendingChangesService;
        _navigateToSetting = navigateToSetting;
        _packHardware = hardwareDetection;
        _packPackages = appxPackages;
        _packActions = pendingActions;
        _packModuleAvailability = moduleAvailabilityLookup;
        if (_packActions is not null) _packActions.PropertyChanged += OnPackActionsChanged;

        foreach (var set in loadResult.Sets.OrderBy(s => s.Name, StringComparer.OrdinalIgnoreCase))
        {
            var item = new SetItemViewModel(set);
            (set.Category == SetCategory.OptimizationPack ? OptimizationPacks : TweakSets).Add(item);
        }

        // The review panel (Discard All / Apply) is reachable while this screen is open;
        // re-resolve so "already staged" tags and conflicts never go stale.
        _pendingChangesService.PropertyChanged += OnPendingChangesChanged;
    }

    public void Dispose()
    {
        _packDisposed = true;
        _pendingChangesService.PropertyChanged -= OnPendingChangesChanged;
        if (_packActions is not null) _packActions.PropertyChanged -= OnPackActionsChanged;
        _packGeneration++;
    }

    private void OnPendingChangesChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(IPendingChangesService.PendingCount) && !_suppressPendingRefresh)
            BuildPreview();
    }

    [RelayCommand]
    private void SelectSet(SetItemViewModel? item)
    {
        if (item is not null)
            SelectedTabIndex = item.Definition.Category == SetCategory.OptimizationPack ? 1 : 0;
        foreach (var set in TweakSets.Concat(OptimizationPacks))
            set.IsSelected = ReferenceEquals(set, item);

        SelectedSet = item;
        PackInspection = InspectPackAsync();
        SearchText = string.Empty;
        StageMessage = string.Empty;
        BuildPreview(preserveChoices: false);
    }

    [RelayCommand]
    private void StageIncluded()
    {
        if (!CanStagePack || _pendingChangesService.IsApplying || _packActions?.IsApplying == true) return;
        var staged = 0;
        var failures = new List<string>();
        // Each Stage/Unstage raises PendingCount; hold the rebuild until the loop is done
        // (BuildPreview would clear the collection being iterated).
        _suppressPendingRefresh = true;
        try
        {
            foreach (var row in PreviewGroups.SelectMany(g => g.Entries))
            {
                if (!row.IsIncluded || !row.CanToggle)
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

                try
                {
                    _pendingChangesService.Stage(group);
                    staged++;
                }
                catch (InvalidOperationException ex)
                {
                    failures.Add($"{group.DisplayName}: {ex.Message}");
                }
            }
        }
        finally
        {
            _suppressPendingRefresh = false;
        }

        var removals = StagePackRemovals();
        StageMessage = staged == 1
            ? "1 change staged. Review and apply from the pending changes bar."
            : $"{staged} changes staged. Review and apply from the pending changes bar.";
        if (failures.Count > 0)
            StageMessage += " " + string.Join(" ", failures);
        if (removals > 0) StageMessage += $" {removals} app removals staged. Removals have no undo.";

        // Re-resolve: freshly staged rows flip to "already staged" and uncheck.
        BuildPreview();
    }

    private void BuildPreview(bool preserveChoices = true)
    {
        // Keep review choices by position, including duplicate entries in user sets.
        var choices = preserveChoices ? PreviewGroups.SelectMany(g => g.Entries)
            .Select(r => (Included: r.CanToggle && !r.IsApplied && !r.IsAlreadyStaged
                && (!r.HasConflict || r.IsIncluded) ? (bool?)r.IsIncluded : null,
                r.Resolution.Conflict, r.Resolution.PendingGroupId, r.Resolution.PendingValue)).ToArray() : [];
        PreviewGroups.Clear();
        if (SelectedSet is null)
        {
            RecountIncluded();
            FilterPreview();
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
                groupVm.Entries.Add(new SetEntryPreviewViewModel(resolution, RecountIncluded, _navigateToSetting));
            PreviewGroups.Add(groupVm);
        }

        var index = 0;
        foreach (var row in PreviewGroups.SelectMany(g => g.Entries))
        {
            if (index < choices.Length && choices[index].Included is { } included && row.CanToggle
                && !row.IsApplied && !row.IsAlreadyStaged && (!included ||
                    row.Resolution.Conflict == choices[index].Conflict
                    && row.Resolution.PendingGroupId == choices[index].PendingGroupId
                    && row.Resolution.PendingValue == choices[index].PendingValue)) row.IsIncluded = included;
            if (_excludedPackModules.Contains(row.Entry.ModuleId)) row.IsIncluded = false;
            index++;
        }

        RecountIncluded();
        FilterPreview();
    }

    private void FilterPreview()
    {
        var query = SearchText.Trim();
        var rows = new List<object>();
        foreach (var group in PreviewGroups)
        {
            var entries = group.Entries.Where(row => query.Length == 0 ||
                new[] { row.SettingName, row.Description, row.Entry.ModuleId, row.Entry.SettingId,
                    row.CurrentDisplay, row.ProposedDisplay, group.GroupName }
                    .Any(text => text.Contains(query, StringComparison.OrdinalIgnoreCase))).ToList();
            if (entries.Count == 0) continue;
            if (group.HasHeader) rows.Add(group);
            rows.AddRange(entries);
        }
        var removals = AppRemovals.Where(row => query.Length == 0 ||
            row.Name.Contains(query, StringComparison.OrdinalIgnoreCase) ||
            row.Description.Contains(query, StringComparison.OrdinalIgnoreCase)).ToList();
        if (removals.Count > 0) rows.Add(new SetPreviewGroupViewModel("App removals · No undo"));
        rows.AddRange(removals);
        VisibleRows = rows;
        OnPropertyChanged(nameof(VisibleRows));
        OnPropertyChanged(nameof(HasNoSearchResults));
    }

    private void RecountIncluded()
        => IncludedCount = PreviewGroups
            .SelectMany(g => g.Entries)
            .Count(e => e.IsIncluded && e.CanToggle) + AppRemovals.Count(r => r.IsIncluded && r.CanInclude);
}
