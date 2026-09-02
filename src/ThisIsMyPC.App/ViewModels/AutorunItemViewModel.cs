using System.ComponentModel;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using ThisIsMyPC.Core.Changes;
using ThisIsMyPC.Core.Services;
using ThisIsMyPC.Modules.Startup.Changes;
using ThisIsMyPC.Modules.Startup.Models;

namespace ThisIsMyPC.App.ViewModels;

/// <summary>One Autoruns category on the Autoruns tab: a header plus its visible rows.</summary>
public sealed class AutorunGroupViewModel
{
    public AutorunGroupViewModel(AutorunCategory category, IReadOnlyList<AutorunItemViewModel> items, int total)
    {
        Category = category;
        Items = items;
        Header = items.Count == total
            ? $"{AutorunEntry.CategoryName(category)} ({total})"
            : $"{AutorunEntry.CategoryName(category)} ({items.Count} of {total})";
    }

    public AutorunCategory Category { get; }
    public string Header { get; }
    public IReadOnlyList<AutorunItemViewModel> Items { get; }
}

/// <summary>
/// One Autoruns row. The switch stages an enable or disable through the
/// pending pipeline; the baseline is the scan-time state, and the row follows
/// the queue the way the Startup rows do (apply keeps the switch, discard
/// snaps it back).
/// </summary>
public sealed partial class AutorunItemViewModel : ObservableObject, IDisposable
{
    private readonly IPendingChangesService _pendingChangesService;
    private bool _liveIsEnabled;
    private string? _stagedGroupId;
    private bool _suppressStaging;
    private bool _isStagingChange;
    private bool _disposed;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(StateText))]
    private bool _isEnabled;

    [ObservableProperty]
    private bool _hasPendingChange;

    public AutorunItemViewModel(AutorunEntry entry, IPendingChangesService pendingChangesService)
    {
        Entry = entry;
        _pendingChangesService = pendingChangesService;
        _liveIsEnabled = entry.IsEnabled;

        _suppressStaging = true;
        IsEnabled = entry.IsEnabled;

        var settingId = AutorunChangeFactory.GetSettingId(entry);
        var existing = pendingChangesService.PendingGroups.FirstOrDefault(g =>
            g.Changes.Count == 1 &&
            g.Changes[0].ModuleId == AutorunChangeFactory.ModuleId &&
            g.Changes[0].SettingId == settingId);
        if (existing is not null)
        {
            var pendingEnabled = existing.Changes[0].Category == ChangeCategory.Enable;
            if (pendingEnabled == _liveIsEnabled)
                pendingChangesService.Unstage(existing.GroupId);
            else
            {
                _stagedGroupId = existing.GroupId;
                IsEnabled = pendingEnabled;
            }
        }

        _suppressStaging = false;
        HasPendingChange = IsEnabled != _liveIsEnabled;
        _pendingChangesService.PropertyChanged += OnPendingChangesPropertyChanged;
    }

    public AutorunEntry Entry { get; }

    public string Name => Entry.Name;
    public string CategoryName => AutorunEntry.CategoryName(Entry.Category);
    public string DescriptionText => Entry.Description ?? string.Empty;
    public bool HasDescription => !string.IsNullOrEmpty(Entry.Description);
    public string PublisherText => Entry.Publisher ?? "Unknown publisher";
    public string ImagePathText => Entry.ImagePath ?? Entry.Data;

    /// <summary>A task's data is its own path; showing it twice says nothing.</summary>
    public bool HasImagePath => !string.Equals(ImagePathText, LocationText, StringComparison.OrdinalIgnoreCase);
    public string NoteText => Entry.Note ?? string.Empty;
    public bool HasNote => !string.IsNullOrEmpty(Entry.Note);
    public string StateText => IsEnabled ? "Enabled" : "Disabled";

    /// <summary>Where the item is registered: key and value, folder and file, task path, or service key.</summary>
    public string LocationText => Entry.Kind switch
    {
        AutorunItemKind.ScheduledTask => Entry.Location,
        _ => $@"{Entry.Location}\{Entry.Name}",
    };

    public bool CanToggle => Entry.CanToggle;

    public bool IsMicrosoft => Entry.Publisher?.Contains("Microsoft", StringComparison.OrdinalIgnoreCase) == true;

    /// <summary>Text the filter box matches against.</summary>
    public bool Matches(string filter)
        => Name.Contains(filter, StringComparison.OrdinalIgnoreCase)
        || DescriptionText.Contains(filter, StringComparison.OrdinalIgnoreCase)
        || PublisherText.Contains(filter, StringComparison.OrdinalIgnoreCase)
        || ImagePathText.Contains(filter, StringComparison.OrdinalIgnoreCase)
        || LocationText.Contains(filter, StringComparison.OrdinalIgnoreCase);

    partial void OnIsEnabledChanged(bool value)
    {
        if (_suppressStaging || _disposed)
            return;

        _isStagingChange = true;
        try
        {
            if (_stagedGroupId is not null)
            {
                _pendingChangesService.Unstage(_stagedGroupId);
                _stagedGroupId = null;
            }

            if (value != _liveIsEnabled)
            {
                var change = AutorunChangeFactory.CreateToggle(Entry with { IsEnabled = _liveIsEnabled }, value);
                var group = new ChangeGroup
                {
                    GroupId = Guid.NewGuid().ToString("N"),
                    DisplayName = change.DisplayName,
                    Description = change.DisplayName,
                    Changes = [change],
                };
                _pendingChangesService.Stage(group);
                _stagedGroupId = group.GroupId;
            }
        }
        finally
        {
            _isStagingChange = false;
        }

        HasPendingChange = IsEnabled != _liveIsEnabled;
    }

    private void OnPendingChangesPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (_isStagingChange || e.PropertyName is not nameof(IPendingChangesService.PendingGroups))
            return;
        if (Dispatcher.UIThread.CheckAccess())
            HandlePendingGroupsChanged();
        else
            Dispatcher.UIThread.Post(HandlePendingGroupsChanged);
    }

    private void HandlePendingGroupsChanged()
    {
        if (_stagedGroupId is null || _pendingChangesService.PendingGroups.Any(g => g.GroupId == _stagedGroupId))
            return;

        _stagedGroupId = null;
        if (_pendingChangesService.IsApplying)
            _liveIsEnabled = IsEnabled;
        else
        {
            _suppressStaging = true;
            IsEnabled = _liveIsEnabled;
            _suppressStaging = false;
        }
        HasPendingChange = IsEnabled != _liveIsEnabled;
    }

    public void Dispose()
    {
        if (_disposed)
            return;
        _disposed = true;
        _pendingChangesService.PropertyChanged -= OnPendingChangesPropertyChanged;
    }
}
