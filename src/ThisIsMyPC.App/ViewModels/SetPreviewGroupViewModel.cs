using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using ThisIsMyPC.Core.Sets;

namespace ThisIsMyPC.App.ViewModels;

/// <summary>
/// One preview section: for optimization packs the constituent tweak set (SetEntry.Group),
/// for tweak sets a single header-less group.
/// </summary>
public sealed class SetPreviewGroupViewModel
{
    public SetPreviewGroupViewModel(string groupName)
    {
        GroupName = groupName;
    }

    public string GroupName { get; }
    public bool HasHeader => GroupName.Length > 0;
    public ObservableCollection<SetEntryPreviewViewModel> Entries { get; } = [];
}

/// <summary>One previewed change row, checkable for staging (8.3).</summary>
public sealed partial class SetEntryPreviewViewModel : ViewModelBase
{
    private readonly Action _includeChanged;

    public SetEntryPreviewViewModel(SetEntryResolution resolution, Action includeChanged)
    {
        Resolution = resolution;
        _includeChanged = includeChanged;
        _isIncluded = resolution.IncludedByDefault;
    }

    public SetEntryResolution Resolution { get; }
    public SetEntry Entry => Resolution.Entry;

    [ObservableProperty]
    private bool _isIncluded;

    partial void OnIsIncludedChanged(bool value) => _includeChanged();

    public string SettingName => Resolution.State?.SettingDisplayName ?? Entry.SettingId;
    public string Description => Entry.Description;
    public string CurrentDisplay => Resolution.State?.CurrentDisplay ?? "—";
    public string CurrentValue => Resolution.State?.CurrentValue ?? "";
    public string ProposedDisplay => Entry.DisplayValue ?? Entry.Value;

    public bool IsSkipped => Resolution.IsSkipped;
    public string? SkipReason => Resolution.SkipReason;
    public bool IsApplied => Resolution.Conflict == SetEntryConflict.AlreadyApplied;
    public bool IsAlreadyStaged => Resolution.Conflict == SetEntryConflict.PendingSameValue;
    public bool HasConflict => Resolution.Conflict == SetEntryConflict.PendingDifferentValue;
    public bool CanToggle => !IsSkipped;
    public string? SkuNotice => Resolution.SkuNotice;
    public bool HasSkuNotice => Resolution.SkuNotice is not null;

    public string ConflictText => HasConflict
        ? $"Conflicts with a pending change: the set wants '{ProposedDisplay}', the pending change sets '{Resolution.PendingDisplay ?? Resolution.PendingValue}', the system currently has '{CurrentDisplay}'. Checking this row replaces the pending change when staged."
        : string.Empty;

    /// <summary>Raw values for the row tooltip; display strings carry the row itself.</summary>
    public string RawValuesTooltip =>
        $"{Entry.ModuleId} / {Entry.SettingId}: '{CurrentValue}' → '{Entry.Value}'";
}
