using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ThisIsMyPC.Core.Sets;

namespace ThisIsMyPC.App.ViewModels;

/// <summary>
/// Inline metadata form for saving a custom set (Story 8.5), shared by the review
/// panel ("Save as Set") and the history panel ("Create Set from Selection").
/// The owner supplies the write callback that packages its own changes.
/// </summary>
public partial class SaveSetFormViewModel : ViewModelBase
{
    private readonly Func<CustomSetMetadata, CustomSetWriteResult> _write;

    public SaveSetFormViewModel(Func<CustomSetMetadata, CustomSetWriteResult> write)
    {
        _write = write;
    }

    [ObservableProperty]
    private bool _isOpen;

    [ObservableProperty]
    private string _setName = string.Empty;

    [ObservableProperty]
    private string _setDescription = string.Empty;

    [ObservableProperty]
    private bool _isOptimizationPack;

    [ObservableProperty]
    private string? _errorMessage;

    [ObservableProperty]
    private string? _successMessage;

    [RelayCommand]
    private void Open()
    {
        SetName = string.Empty;
        SetDescription = string.Empty;
        IsOptimizationPack = false;
        ErrorMessage = null;
        SuccessMessage = null;
        IsOpen = true;
    }

    [RelayCommand]
    private void Cancel()
    {
        IsOpen = false;
        ErrorMessage = null;
    }

    [RelayCommand]
    private async Task SaveAsync()
    {
        ErrorMessage = null;

        var metadata = new CustomSetMetadata
        {
            Name = SetName,
            Description = SetDescription,
            Category = IsOptimizationPack ? SetCategory.OptimizationPack : SetCategory.TweakSet,
        };

        // File I/O off the UI thread; slow disk must not freeze the popup.
        var result = await Task.Run(() => _write(metadata)).ConfigureAwait(true);

        if (!result.Success)
        {
            ErrorMessage = result.Error;
            return;
        }

        IsOpen = false;
        var changes = result.EntryCount == 1 ? "1 change" : $"{result.EntryCount} changes";
        var skipped = result.SkippedGroupCount > 0 ? $", {result.SkippedGroupCount} skipped" : string.Empty;
        SuccessMessage = $"Saved {Path.GetFileName(result.FilePath)} ({changes}{skipped})";
    }
}
