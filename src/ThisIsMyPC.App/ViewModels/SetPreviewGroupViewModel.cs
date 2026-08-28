using System.Collections.ObjectModel;
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

/// <summary>One previewed change row.</summary>
public sealed class SetEntryPreviewViewModel
{
    private SetEntryPreviewViewModel(
        SetEntry entry, string settingName, string currentDisplay, string currentValue,
        bool isApplied, bool isSkipped, string? skipReason)
    {
        Entry = entry;
        SettingName = settingName;
        CurrentDisplay = currentDisplay;
        CurrentValue = currentValue;
        IsApplied = isApplied;
        IsSkipped = isSkipped;
        SkipReason = skipReason;
    }

    public static SetEntryPreviewViewModel Resolved(SetEntry entry, SetEntryState state)
        => new(entry, state.SettingDisplayName, state.CurrentDisplay, state.CurrentValue,
            state.IsApplied, isSkipped: false, skipReason: null);

    public static SetEntryPreviewViewModel Skipped(SetEntry entry, string reason)
        => new(entry, entry.SettingId, currentDisplay: "—", currentValue: "",
            isApplied: false, isSkipped: true, skipReason: reason);

    public SetEntry Entry { get; }
    public string SettingName { get; }
    public string Description => Entry.Description;
    public string CurrentDisplay { get; }
    public string CurrentValue { get; }
    public string ProposedDisplay => Entry.DisplayValue ?? Entry.Value;
    public bool IsApplied { get; }
    public bool IsSkipped { get; }
    public string? SkipReason { get; }

    /// <summary>Raw values for the row tooltip; display strings carry the row itself.</summary>
    public string RawValuesTooltip =>
        $"{Entry.ModuleId} / {Entry.SettingId}: '{CurrentValue}' → '{Entry.Value}'";
}
