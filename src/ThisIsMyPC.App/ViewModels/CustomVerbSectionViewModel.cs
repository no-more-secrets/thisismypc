using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ThisIsMyPC.Core.Services;
using ThisIsMyPC.Modules.Shell.Changes;
using ThisIsMyPC.Modules.Shell.Models;
using ThisIsMyPC.Modules.Shell.Services;

namespace ThisIsMyPC.App.ViewModels;

/// <summary>
/// The Custom tab of the Context Menus module (2-6): lists ThisIsMyPC-created
/// entries and hosts the create/edit form. Everything stages through the pending
/// pipeline — the list shows the applied registry state, so a freshly staged
/// entry appears here only after Apply (the review panel carries it until then).
/// </summary>
public partial class CustomVerbSectionViewModel : ViewModelBase
{
    private readonly IPendingChangesService _pendingChanges;
    private readonly CustomVerbService _service;

    private CustomVerbDefinition? _editing;

    public ObservableCollection<CustomVerbEntryViewModel> Entries { get; } = [];

    public IReadOnlyList<string> ScopeChoices { get; } =
        CustomVerbDefinition.ScopeOptions.Select(o => o.DisplayName).ToList();

    [ObservableProperty]
    private bool _isFormOpen;

    [ObservableProperty]
    private string _formHeader = "";

    [ObservableProperty]
    private string _formLabel = "";

    [ObservableProperty]
    private string _formCommandLine = "";

    [ObservableProperty]
    private string _formIconPath = "";

    [ObservableProperty]
    private string _selectedScopeChoice = "All files";

    [ObservableProperty]
    private bool _isScopeEditable = true;

    [ObservableProperty]
    private string _formError = "";

    public CustomVerbSectionViewModel(IPendingChangesService pendingChanges, IRegistryService registry)
    {
        _pendingChanges = pendingChanges;
        _service = new CustomVerbService(registry);
        Refresh();
    }

    [RelayCommand]
    private void Refresh()
    {
        Entries.Clear();
        foreach (var definition in _service.Enumerate()
                     .OrderBy(d => d.ScopeDisplayName).ThenBy(d => d.Label, StringComparer.OrdinalIgnoreCase))
        {
            Entries.Add(new CustomVerbEntryViewModel(definition));
        }
    }

    [RelayCommand]
    private void OpenCreateForm()
    {
        _editing = null;
        FormHeader = "New custom entry";
        FormLabel = "";
        FormCommandLine = "";
        FormIconPath = "";
        SelectedScopeChoice = ScopeChoices[0];
        IsScopeEditable = true;
        FormError = "";
        IsFormOpen = true;
    }

    [RelayCommand]
    private void BeginEdit(CustomVerbEntryViewModel entry)
    {
        _editing = entry.Definition;
        FormHeader = $"Edit \"{entry.Definition.Label}\"";
        FormLabel = entry.Definition.Label;
        FormCommandLine = entry.Definition.Command;
        FormIconPath = entry.Definition.IconPath ?? "";
        SelectedScopeChoice = entry.Definition.ScopeDisplayName;
        IsScopeEditable = false; // moving scopes is delete + recreate, not edit
        FormError = "";
        IsFormOpen = true;
    }

    [RelayCommand]
    private void CancelForm() => IsFormOpen = false;

    [RelayCommand]
    private void Submit()
    {
        var label = FormLabel.Trim();
        var command = FormCommandLine.Trim();
        if (label.Length == 0 || command.Length == 0)
        {
            FormError = "Label and command are both required.";
            return;
        }

        var icon = FormIconPath.Trim();
        if (_editing is { } before)
        {
            var after = before with
            {
                Label = label,
                Command = command,
                IconPath = icon.Length > 0 ? icon : null,
            };
            if (after != before)
                StageReplacing(CustomVerbChangeFactory.CreateEdit(before, after));
        }
        else
        {
            var scope = CustomVerbDefinition.ScopeOptions
                .First(o => o.DisplayName == SelectedScopeChoice).Scope;
            var definition = new CustomVerbDefinition
            {
                Scope = scope,
                VerbId = UniqueVerbId(scope, CustomVerbChangeFactory.MakeVerbId(label)),
                Label = label,
                Command = command,
                IconPath = icon.Length > 0 ? icon : null,
            };
            StageReplacing(CustomVerbChangeFactory.CreateNew(definition));
        }

        IsFormOpen = false;
    }

    [RelayCommand]
    private void Delete(CustomVerbEntryViewModel entry) =>
        StageReplacing(CustomVerbChangeFactory.CreateDelete(entry.Definition));

    /// <summary>
    /// One pending group per entry: staging supersedes any earlier staged group for
    /// the same SettingId (double-stage duplicates, delete-then-edit resurrection).
    /// </summary>
    private void StageReplacing(Core.Changes.ChangeDescriptor change)
    {
        var stale = _pendingChanges.PendingGroups
            .Where(g => g.Changes.Any(c => c.SettingId == change.SettingId))
            .Select(g => g.GroupId)
            .ToList();
        foreach (var groupId in stale)
            _pendingChanges.Unstage(groupId);
        _pendingChanges.Stage(change);
    }

    private string UniqueVerbId(string scope, string baseId)
    {
        var taken = Entries
            .Where(e => e.Definition.Scope.Equals(scope, StringComparison.OrdinalIgnoreCase))
            .Select(e => e.Definition.VerbId)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        // Staged-but-unapplied creates are invisible in Entries (the list shows the
        // applied registry state) — without this, two same-label creates would share
        // a key path and silently overwrite each other on Apply.
        foreach (var group in _pendingChanges.PendingGroups)
        {
            foreach (var change in group.Changes)
            {
                if (change.ValueType != Core.Changes.ChangeValueType.Shell_CustomVerb)
                    continue;
                var staged = CustomVerbDefinition.Deserialize(change.AfterValue)
                    ?? CustomVerbDefinition.Deserialize(change.BeforeValue);
                if (staged is not null && staged.Scope.Equals(scope, StringComparison.OrdinalIgnoreCase))
                    taken.Add(staged.VerbId);
            }
        }

        if (!taken.Contains(baseId))
            return baseId;
        for (var i = 2; ; i++)
        {
            var candidate = $"{baseId}-{i}";
            if (!taken.Contains(candidate))
                return candidate;
        }
    }
}

public sealed class CustomVerbEntryViewModel
{
    public CustomVerbEntryViewModel(CustomVerbDefinition definition) => Definition = definition;

    public CustomVerbDefinition Definition { get; }

    public string Label => Definition.Label;
    public string Command => Definition.Command;
    public string ScopeDisplay => Definition.ScopeDisplayName;
    public bool HasIcon => Definition.IconPath is not null;
}
