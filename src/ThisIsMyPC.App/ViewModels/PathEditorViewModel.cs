using System.Collections.ObjectModel;
using System.Text;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
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

        var entries = (currentPath ?? string.Empty)
            .Split(';', StringSplitOptions.RemoveEmptyEntries);

        for (var i = 0; i < entries.Length; i++)
        {
            Entries.Add(new PathEntryViewModel(entries[i], i + 1));
        }

        UpdateCharacterCount();
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
        Entries.Add(new PathEntryViewModel(string.Empty, Entries.Count + 1));
        ReindexAndStage();
    }

    [RelayCommand]
    private void RemoveEntry(PathEntryViewModel entry)
    {
        Entries.Remove(entry);
        ReindexAndStage();
    }

    [RelayCommand]
    private void SavePath()
    {
        ReindexAndStage();
    }

    private void ReindexAndStage()
    {
        for (var i = 0; i < Entries.Count; i++)
            Entries[i].Index = i + 1;

        UpdateCharacterCount();

        var newPath = string.Join(';', Entries.Select(e => e.Path));
        if (newPath == _originalPath)
            return;

        var diff = GenerateDiff(_originalPath, newPath);
        var change = EnvironmentVariableChangeFactory.CreatePathEdit(
            _scope, _registryKeyPath, _originalPath, newPath, diff);
        _pendingChangesService.Stage(change);
    }

    private void UpdateCharacterCount()
    {
        var total = Entries.Sum(e => e.Path.Length) + Math.Max(0, Entries.Count - 1); // semicolons
        var warning = total > 2048 ? " (WARNING: exceeds 2048 char practical limit)" : "";
        CharacterCountText = $"{total} characters{warning}";
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
