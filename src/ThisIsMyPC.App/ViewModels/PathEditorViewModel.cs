using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Text;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ThisIsMyPC.Core.Changes;
using ThisIsMyPC.Core.Services;
using ThisIsMyPC.Modules.Shell.Changes;
using ThisIsMyPC.Modules.Shell.Services;

namespace ThisIsMyPC.App.ViewModels;

public partial class PathEditorViewModel : ViewModelBase
{
    private readonly string _scope;
    private readonly string _registryKeyPath;
    private readonly string _originalPath;
    private readonly IPendingChangesService _pendingChangesService;

    private string? _stagedGroupId;
    private bool _isStagingChange;

    public ObservableCollection<PathEntryViewModel> Entries { get; } = [];

    [ObservableProperty]
    private string _characterCountText = string.Empty;

    public PathEditorViewModel(
        string currentPath,
        string scope,
        IPendingChangesService pendingChangesService)
    {
        _originalPath = currentPath;
        _scope = scope;
        _pendingChangesService = pendingChangesService;
        _registryKeyPath = string.Equals(scope, "User", StringComparison.OrdinalIgnoreCase)
            ? EnvironmentVariableReader.UserEnvKeyPath
            : EnvironmentVariableReader.SystemEnvKeyPath;

        PopulateEntries(currentPath);
        UpdateCharacterCount();

        _pendingChangesService.PropertyChanged += OnPendingChangesPropertyChanged;
    }

    public void MoveEntry(int fromIndex, int toIndex)
    {
        if (fromIndex < 0 || fromIndex >= Entries.Count) return;
        if (toIndex < 0 || toIndex >= Entries.Count) return;
        if (fromIndex == toIndex) return;

        Entries.Move(fromIndex, toIndex);
        ReindexAndStage();
    }

    [RelayCommand]
    private void AddEntry()
    {
        var entry = new PathEntryViewModel(string.Empty, Entries.Count + 1);
        SubscribeToEntry(entry);
        Entries.Add(entry);
        ReindexAndStage();
    }

    [RelayCommand]
    private void RemoveEntry(PathEntryViewModel entry)
    {
        UnsubscribeFromEntry(entry);
        Entries.Remove(entry);
        ReindexAndStage();
    }

    private void ReindexAndStage()
    {
        for (var i = 0; i < Entries.Count; i++)
            Entries[i].Index = i + 1;

        UpdateCharacterCount();

        var newPath = string.Join(';', Entries.Select(e => e.Path));

        _isStagingChange = true;
        try
        {
            // Unstage previous version to avoid accumulating duplicate pending changes
            if (_stagedGroupId is not null)
            {
                _pendingChangesService.Unstage(_stagedGroupId);
                _stagedGroupId = null;
            }

            if (newPath == _originalPath)
                return;

            var diff = GenerateDiff(_originalPath, newPath);
            var change = EnvironmentVariableChangeFactory.CreatePathEdit(
                _scope, _registryKeyPath, _originalPath, newPath, diff);

            var group = new ChangeGroup
            {
                GroupId = Guid.NewGuid().ToString("N"),
                DisplayName = change.DisplayName,
                Description = change.DisplayName,
                Changes = [change]
            };

            _pendingChangesService.Stage(group);
            _stagedGroupId = group.GroupId;
        }
        finally
        {
            _isStagingChange = false;
        }
    }

    private void OnPendingChangesPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (_isStagingChange)
            return;
        if (e.PropertyName is not nameof(IPendingChangesService.PendingGroups))
            return;

        // Our staged change was removed — either applied or discarded
        if (_stagedGroupId is not null &&
            !_pendingChangesService.PendingGroups.Any(g => g.GroupId == _stagedGroupId))
        {
            _stagedGroupId = null;

            if (!_pendingChangesService.IsApplying)
            {
                // Change was discarded — reset to original
                Reset();
            }
        }
    }

    private void OnEntryPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(PathEntryViewModel.Path))
            ReindexAndStage();
    }

    private void SubscribeToEntry(PathEntryViewModel entry)
    {
        entry.PropertyChanged += OnEntryPropertyChanged;
    }

    private void UnsubscribeFromEntry(PathEntryViewModel entry)
    {
        entry.PropertyChanged -= OnEntryPropertyChanged;
    }

    private void Reset()
    {
        foreach (var entry in Entries)
            UnsubscribeFromEntry(entry);

        Entries.Clear();
        PopulateEntries(_originalPath);
        UpdateCharacterCount();
    }

    private void PopulateEntries(string path)
    {
        var entries = (path ?? string.Empty)
            .Split(';', StringSplitOptions.RemoveEmptyEntries);

        for (var i = 0; i < entries.Length; i++)
        {
            var entry = new PathEntryViewModel(entries[i], i + 1);
            SubscribeToEntry(entry);
            Entries.Add(entry);
        }
    }

    private void UpdateCharacterCount()
    {
        var total = Entries.Sum(e => e.Path.Length) + Math.Max(0, Entries.Count - 1); // semicolons
        var warning = total > 2048 ? " (WARNING: exceeds 2048 char practical limit)" : "";
        CharacterCountText = $"{Entries.Count} entries, {total} characters{warning}";
    }

    public static string GenerateDiff(string oldPath, string newPath)
    {
        var oldEntries = (oldPath ?? string.Empty).Split(';', StringSplitOptions.RemoveEmptyEntries).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var newEntries = (newPath ?? string.Empty).Split(';', StringSplitOptions.RemoveEmptyEntries).ToHashSet(StringComparer.OrdinalIgnoreCase);

        var sb = new StringBuilder();

        foreach (var entry in newEntries.Except(oldEntries, StringComparer.OrdinalIgnoreCase))
            sb.AppendLine($"+Added: {entry}");

        foreach (var entry in oldEntries.Except(newEntries, StringComparer.OrdinalIgnoreCase))
            sb.AppendLine($"-Removed: {entry}");

        if (sb.Length == 0 && oldPath != newPath)
            sb.AppendLine("Reordered entries");

        return sb.ToString().TrimEnd();
    }
}
