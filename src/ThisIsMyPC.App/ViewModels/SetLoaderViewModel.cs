using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ThisIsMyPC.Core.Modules;
using ThisIsMyPC.Core.Sets;

namespace ThisIsMyPC.App.ViewModels;

/// <summary>
/// The Set Loader screen (8.2): browse loaded sets by category and preview every change
/// with its live current value. Staging and conflict resolution arrive in 8.3.
/// </summary>
public partial class SetLoaderViewModel : ViewModelBase
{
    private readonly IReadOnlyList<ISetEntryInspector> _inspectors;
    private readonly Func<string, ModuleAvailability?> _moduleAvailabilityLookup;

    public ObservableCollection<SetItemViewModel> TweakSets { get; } = [];
    public ObservableCollection<SetItemViewModel> OptimizationPacks { get; } = [];
    public ObservableCollection<SetPreviewGroupViewModel> PreviewGroups { get; } = [];

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasNoSelection))]
    private SetItemViewModel? _selectedSet;

    public bool HasTweakSets => TweakSets.Count > 0;
    public bool HasOptimizationPacks => OptimizationPacks.Count > 0;
    public bool HasNoSets => TweakSets.Count == 0 && OptimizationPacks.Count == 0;
    public bool HasNoSelection => SelectedSet is null;

    public SetLoaderViewModel(
        SetLoadResult loadResult,
        IEnumerable<ISetEntryInspector> inspectors,
        Func<string, ModuleAvailability?> moduleAvailabilityLookup)
    {
        _inspectors = inspectors.ToList();
        _moduleAvailabilityLookup = moduleAvailabilityLookup;

        foreach (var set in loadResult.Sets.OrderBy(s => s.Name, StringComparer.OrdinalIgnoreCase))
        {
            var item = new SetItemViewModel(set);
            (set.Category == SetCategory.OptimizationPack ? OptimizationPacks : TweakSets).Add(item);
        }
    }

    [RelayCommand]
    private void SelectSet(SetItemViewModel? item)
    {
        foreach (var set in TweakSets.Concat(OptimizationPacks))
            set.IsSelected = ReferenceEquals(set, item);

        SelectedSet = item;
        BuildPreview();
    }

    private void BuildPreview()
    {
        PreviewGroups.Clear();
        if (SelectedSet is null)
            return;

        var definition = SelectedSet.Definition;
        var grouped = definition.Category == SetCategory.OptimizationPack
            ? definition.Entries.GroupBy(e => e.Group ?? "Other")
            : definition.Entries.GroupBy(_ => string.Empty);

        foreach (var group in grouped)
        {
            var groupVm = new SetPreviewGroupViewModel(group.Key);
            foreach (var entry in group)
                groupVm.Entries.Add(BuildEntryPreview(entry));
            PreviewGroups.Add(groupVm);
        }
    }

    private SetEntryPreviewViewModel BuildEntryPreview(SetEntry entry)
    {
        var availability = _moduleAvailabilityLookup(entry.ModuleId);
        if (availability is null)
        {
            return SetEntryPreviewViewModel.Skipped(entry,
                $"Will be skipped — the '{entry.ModuleId}' module is not part of this build.");
        }

        if (!availability.IsAvailable)
        {
            return SetEntryPreviewViewModel.Skipped(entry,
                $"Will be skipped — {availability.Reason ?? $"the '{entry.ModuleId}' module is not available on this system"}.");
        }

        var state = _inspectors.FirstOrDefault(i => i.ModuleId == entry.ModuleId)?.Inspect(entry);
        if (state is null)
        {
            return SetEntryPreviewViewModel.Skipped(entry,
                "Will be skipped — this setting is not recognized by the installed version.");
        }

        return SetEntryPreviewViewModel.Resolved(entry, state);
    }
}
