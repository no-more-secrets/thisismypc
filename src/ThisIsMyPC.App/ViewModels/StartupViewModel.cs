using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using ThisIsMyPC.Core.Changes;
using ThisIsMyPC.Core.Services;
using ThisIsMyPC.Modules.Startup.Changes;
using ThisIsMyPC.Modules.Startup.Models;

namespace ThisIsMyPC.App.ViewModels;

/// <summary>
/// Discovered startup entries grouped by source type, with enable/disable
/// toggles staged through the pending-changes pipeline (StartupApproved state).
/// </summary>
public sealed partial class StartupViewModel : ObservableObject, IDisposable
{
    [ObservableProperty]
    private bool _isRegistryViewMode;

    public StartupViewModel(StartupScanData scanData, IPendingChangesService pendingChangesService, IRegistryService registryService)
    {
        RegistryEntries = new ObservableCollection<StartupEntryItemViewModel>(
            scanData.StartupEntries
                .Where(e => e.Source is StartupSource.RegistryMachineRun or StartupSource.RegistryMachineRunWow64 or StartupSource.RegistryUserRun)
                .OrderBy(e => e.Name, StringComparer.OrdinalIgnoreCase)
                .Select(e => new StartupEntryItemViewModel(e, pendingChangesService, registryService)));
        FolderEntries = new ObservableCollection<StartupEntryItemViewModel>(
            scanData.StartupEntries
                .Where(e => e.Source is StartupSource.StartupFolderUser or StartupSource.StartupFolderCommon)
                .OrderBy(e => e.Name, StringComparer.OrdinalIgnoreCase)
                .Select(e => new StartupEntryItemViewModel(e, pendingChangesService, registryService)));
        TaskEntries = new ObservableCollection<StartupEntryItemViewModel>(
            scanData.StartupEntries
                .Where(e => e.Source == StartupSource.ScheduledTask)
                .OrderBy(e => e.Name, StringComparer.OrdinalIgnoreCase)
                .Select(e => new StartupEntryItemViewModel(e, pendingChangesService, registryService)));
    }

    public ObservableCollection<StartupEntryItemViewModel> RegistryEntries { get; }
    public ObservableCollection<StartupEntryItemViewModel> FolderEntries { get; }
    public ObservableCollection<StartupEntryItemViewModel> TaskEntries { get; }

    public string RegistryHeader => $"Registry ({RegistryEntries.Count})";
    public string FolderHeader => $"Startup Folder ({FolderEntries.Count})";
    public string TaskHeader => $"Scheduled Tasks ({TaskEntries.Count})";

    public bool HasRegistryEntries => RegistryEntries.Count > 0;
    public bool HasFolderEntries => FolderEntries.Count > 0;
    public bool HasTaskEntries => TaskEntries.Count > 0;

    partial void OnIsRegistryViewModeChanged(bool value)
    {
        foreach (var item in RegistryEntries.Concat(FolderEntries).Concat(TaskEntries))
            item.IsRegistryViewMode = value;
    }

    public void Dispose()
    {
        foreach (var item in RegistryEntries.Concat(FolderEntries).Concat(TaskEntries))
            item.Dispose();
    }
}

public sealed partial class StartupEntryItemViewModel : ObservableObject, IDisposable
{
    private readonly IPendingChangesService _pendingChangesService;
    private readonly IRegistryService _registryService;
    private bool _liveIsEnabled;
    private bool _suppressStaging;
    private bool _isStagingChange;
    private string? _stagedGroupId;
    private bool _disposed;

    [ObservableProperty]
    private bool _isRegistryViewMode;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(StateText))]
    private bool _isEnabled;

    [ObservableProperty]
    private bool _hasPendingChange;

    [ObservableProperty]
    private bool _isPendingEnable;

    [ObservableProperty]
    private bool _isPendingDisable;

    public StartupEntryItemViewModel(StartupEntry entry, IPendingChangesService pendingChangesService, IRegistryService registryService)
    {
        Entry = entry;
        _pendingChangesService = pendingChangesService;
        _registryService = registryService;
        _liveIsEnabled = entry.IsEnabled;

        _suppressStaging = true;
        IsEnabled = entry.IsEnabled;

        // Rehydrate a group this module staged in an earlier visit so re-navigation
        // shows the pending state instead of double-staging the same entry.
        var settingId = StartupChangeFactory.GetSettingId(entry);
        var existing = pendingChangesService.PendingGroups.FirstOrDefault(g =>
            g.Changes.Count == 1 &&
            g.Changes[0].ModuleId == "Startup & Services" &&
            g.Changes[0].SettingId == settingId);
        if (existing is not null)
        {
            _stagedGroupId = existing.GroupId;
            IsEnabled = existing.Changes[0].Category == ChangeCategory.Enable;
        }

        _suppressStaging = false;
        UpdatePendingState();

        _pendingChangesService.PropertyChanged += OnPendingChangesPropertyChanged;
    }

    public StartupEntry Entry { get; }

    public string Name => Entry.Name;
    public string PublisherText => Entry.Publisher ?? "Unknown publisher";
    public string DescriptionText => Entry.Description ?? string.Empty;
    public bool HasDescription => !string.IsNullOrEmpty(Entry.Description);
    public string StateText => IsEnabled ? "Enabled" : "Disabled";

    /// <summary>Scheduled-task entries can't be toggled until Story 3.4's COM interop.</summary>
    public bool CanToggle => StartupChangeFactory.GetApprovedKeyPath(Entry.Source) is not null;

    /// <summary>Simplified view: the executable that runs (fallback to the raw command).</summary>
    public string FileLocationText => Entry.ExecutablePath ?? Entry.Command;

    /// <summary>Registry view: exact registry key / folder / task path plus the raw command.</summary>
    public string RegistryLocationText => $@"{Entry.SourceLocation}\{Entry.Name}";

    public string SourceLabel => Entry.Source switch
    {
        StartupSource.RegistryMachineRun => "Registry (all users)",
        StartupSource.RegistryMachineRunWow64 => "Registry (all users, 32-bit)",
        StartupSource.RegistryUserRun => "Registry (current user)",
        StartupSource.StartupFolderUser => "Startup folder (current user)",
        StartupSource.StartupFolderCommon => "Startup folder (all users)",
        StartupSource.ScheduledTask => "Scheduled task",
        _ => "Unknown",
    };

    partial void OnIsEnabledChanged(bool value)
    {
        if (_suppressStaging || _disposed || !CanToggle)
            return;

        try
        {
            // Refresh baseline from the live StartupApproved state (source of truth)
            var approvedKey = StartupChangeFactory.GetApprovedKeyPath(Entry.Source)!;
            var blobResult = _registryService.ReadBinary(approvedKey, Entry.Name);
            var currentBlob = blobResult.IsSuccess ? blobResult.Value : null;
            _liveIsEnabled = currentBlob is null || currentBlob.Length == 0 || (currentBlob[0] & 1) == 0;

            var change = StartupChangeFactory.CreateToggle(Entry, value, currentBlob);
            if (change is null)
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

            UpdatePendingState();
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Toggle staging failed for {Name}: {ex.Message}");
        }
    }

    private void OnPendingChangesPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (_isStagingChange)
            return;
        if (e.PropertyName is not nameof(IPendingChangesService.PendingGroups))
            return;

        if (Dispatcher.UIThread.CheckAccess())
            HandlePendingGroupsChanged();
        else
            Dispatcher.UIThread.Post(HandlePendingGroupsChanged);
    }

    private void HandlePendingGroupsChanged()
    {
        // Our staged change was removed — either applied or discarded
        if (_stagedGroupId is not null &&
            !_pendingChangesService.PendingGroups.Any(g => g.GroupId == _stagedGroupId))
        {
            _stagedGroupId = null;

            if (_pendingChangesService.IsApplying)
            {
                // Change was applied — keep toggle position, update baseline to match
                _liveIsEnabled = IsEnabled;
            }
            else
            {
                // Change was discarded — reset toggle to live state
                _suppressStaging = true;
                IsEnabled = _liveIsEnabled;
                _suppressStaging = false;
            }

            UpdatePendingState();
        }
    }

    private void UpdatePendingState()
    {
        HasPendingChange = IsEnabled != _liveIsEnabled;
        IsPendingEnable = HasPendingChange && IsEnabled;
        IsPendingDisable = HasPendingChange && !IsEnabled;
    }

    public void Dispose()
    {
        if (_disposed)
            return;
        _disposed = true;
        _pendingChangesService.PropertyChanged -= OnPendingChangesPropertyChanged;
    }
}
