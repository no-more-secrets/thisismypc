using System.ComponentModel;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ThisIsMyPC.Core.Changes;
using ThisIsMyPC.Core.Services;
using ThisIsMyPC.Modules.Shell.Changes;
using ThisIsMyPC.Modules.Shell.Models;

namespace ThisIsMyPC.App.ViewModels;

public sealed partial class ContextMenuHandlerViewModel : ViewModelBase, IDisposable
{
    private readonly ContextMenuHandler _handler;
    private readonly IPendingChangesService _pendingChangesService;
    private readonly Func<bool>? _readRegistryState;
    private bool _registryIsEnabled;
    private bool _suppressStaging;
    private bool _isStagingChange;
    private bool _disposed;
    private bool _orphanCleanupStaged;
    private string? _stagedGroupId;
    private CancellationTokenSource? _debounceCts;

    [ObservableProperty]
    private string _label = string.Empty;

    [ObservableProperty]
    private string _description = string.Empty;

    [ObservableProperty]
    private string _systemPath = string.Empty;

    [ObservableProperty]
    private bool _isEnabled;

    [ObservableProperty]
    private bool _hasPendingChange;

    [ObservableProperty]
    private bool _isPendingEnable;

    [ObservableProperty]
    private bool _isPendingDisable;

    [ObservableProperty]
    private bool _canMigrate;

    public HandlerClassification Classification { get; }
    public HandlerType HandlerType { get; }
    public IReadOnlyList<string> AllScopes { get; }
    public string ScopeNote { get; private set; }
    public string Clsid { get; }
    public string? DllPath { get; }
    public IReadOnlyList<string> AllRegistryPaths { get; }
    public MiscSurfaceGroup? MiscGroup { get; set; }
    public string WarningText { get; }
    public string DisableMethodText { get; }
    public string HandlerTypeBadge { get; }
    public StaticVerbInfo? VerbInfo { get; }
    public ModernPackagedInfo? PackagedInfo { get; }
    public bool IsDualRegistered { get; }
    public string? DualRegistrationPartnerName { get; }
    public string? DualRegistrationNote { get; }
    public bool IsToggleEnabled { get; }
    public string? ToggleDisabledTooltip { get; }
    public bool IsOrphaned { get; }
    public string? OrphanReason { get; }
    public ContextMenuHandler Handler => _handler;

    public ContextMenuHandlerViewModel(
        ContextMenuHandler handler,
        IPendingChangesService pendingChangesService,
        Func<bool>? readRegistryState = null)
    {
        _handler = handler;
        _pendingChangesService = pendingChangesService;
        _readRegistryState = readRegistryState;
        _registryIsEnabled = handler.IsEnabled;

        Classification = handler.Classification;
        HandlerType = handler.HandlerType;
        VerbInfo = handler.VerbInfo;
        PackagedInfo = handler.PackagedInfo;
        IsDualRegistered = handler.IsDualRegistered;
        DualRegistrationPartnerName = handler.DualRegistrationPartnerName;
        AllScopes = handler.AllScopes ?? [handler.AppliesTo];
        Clsid = handler.Clsid;
        DllPath = handler.DllPath;
        AllRegistryPaths = handler.AllRegistryPaths ?? [handler.RegistryPath];

        Label = handler.HandlerType == HandlerType.StaticVerb
            ? handler.VerbInfo?.MuiVerb ?? handler.Name
            : handler.Name;
        ScopeNote = string.Empty; // Set after tab assignment via SetScopeNote
        Description = BuildDescription(handler);
        SystemPath = handler.RegistryPath;
        WarningText = BuildWarningText(handler);
        CanMigrate = handler.DisableMethod == DisableMethod.DashPrefix;
        DisableMethodText = handler.DisableMethod switch
        {
            DisableMethod.DashPrefix => "Disabled via dash-prefix (legacy)",
            DisableMethod.BlockedList => "Disabled via Blocked List",
            DisableMethod.Both => "Disabled via Blocked List + dash-prefix (legacy)",
            _ => string.Empty,
        };
        HandlerTypeBadge = handler.IsOrphaned
            ? "Orphaned"
            : handler.HandlerType switch
            {
                HandlerType.StaticVerb => "Static Verb",
                HandlerType.ModernPackaged => "Modern Packaged",
                HandlerType.DragDropHandler => "Drag-Drop",
                _ => "COM Handler",
            };

        // Orphan properties
        IsOrphaned = handler.IsOrphaned;
        OrphanReason = handler.OrphanReason;

        // Modern handlers cannot be toggled via registry; orphaned handlers should use Clean Up instead
        if (handler.IsOrphaned)
        {
            IsToggleEnabled = false;
            ToggleDisabledTooltip = "This handler's DLL is missing — use Clean Up to remove the orphaned registration";
        }
        else if (handler.HandlerType == HandlerType.ModernPackaged)
        {
            IsToggleEnabled = false;
            ToggleDisabledTooltip = "Modern handlers are managed at the package level (Settings > Apps)";
        }
        else if (handler.HandlerType == HandlerType.DragDropHandler)
        {
            IsToggleEnabled = false;
            ToggleDisabledTooltip = "Drag-drop handlers can only be removed by uninstalling the application";
        }
        else
        {
            IsToggleEnabled = true;
            ToggleDisabledTooltip = null;
        }

        // Dual-registration cross-reference note
        if (handler.IsDualRegistered && handler.DualRegistrationPartnerName is not null)
        {
            var partnerType = handler.HandlerType == HandlerType.ModernPackaged
                ? "COM Handler"
                : "Modern Packaged";
            DualRegistrationNote = $"Also registered as {partnerType}: {handler.DualRegistrationPartnerName}";
        }

        _suppressStaging = true;
        IsEnabled = handler.IsEnabled;
        _suppressStaging = false;

        _pendingChangesService.PropertyChanged += OnPendingChangesPropertyChanged;
    }

    public void SetScopeNote(string scopeNote)
    {
        ScopeNote = scopeNote;
        Description = BuildDescription(_handler, scopeNote);
    }

    public void SetRegistryViewMode(bool isRegistryView)
    {
        if (isRegistryView)
        {
            if (HandlerType == HandlerType.StaticVerb && VerbInfo is not null)
            {
                var parts = new List<string> { $"Verb: {VerbInfo.VerbName}" };
                if (VerbInfo.CommandLine is not null)
                    parts.Add($"Command: {VerbInfo.CommandLine}");
                if (VerbInfo.DelegateExecuteClsid is not null)
                    parts.Add($"DelegateExecute: {VerbInfo.DelegateExecuteClsid}");
                if (VerbInfo.AppliesTo is not null)
                    parts.Add($"AppliesTo: {VerbInfo.AppliesTo}");
                Description = string.Join(" | ", parts);
            }
            else if (HandlerType == HandlerType.ModernPackaged && PackagedInfo is not null)
            {
                var parts = new List<string> { $"Package: {PackagedInfo.PackageFamilyName}" };
                if (!string.IsNullOrEmpty(Clsid))
                    parts.Add($"CLSID: {Clsid}");
                if (PackagedInfo.ItemTypes is { Count: > 0 })
                    parts.Add($"ItemTypes: {string.Join(", ", PackagedInfo.ItemTypes)}");
                Description = string.Join(" | ", parts);
            }
            else if (IsOrphaned)
            {
                Description = $"ORPHANED | CLSID: {Clsid} | {OrphanReason ?? "DLL missing"}";
            }
            else
            {
                var regKeyName = _handler.RegistryKeyName;
                Description = regKeyName is not null && regKeyName != _handler.Name
                    ? $"CLSID: {Clsid} | Key: {regKeyName}"
                    : Clsid;
            }
            SystemPath = string.Join("\n", AllRegistryPaths);
            if (DllPath is not null)
                SystemPath += $"\nDLL: {DllPath}";
        }
        else
        {
            Description = BuildDescription(_handler, ScopeNote);
            SystemPath = _handler.RegistryPath;
        }
    }

    private static string BuildDescription(ContextMenuHandler handler, string? scopeNote = null)
    {
        if (handler.IsOrphaned)
        {
            var orphanText = $"ORPHANED — {handler.OrphanReason ?? "DLL missing"}";
            if (!string.IsNullOrEmpty(scopeNote))
                orphanText += $" -- {scopeNote}";
            return orphanText;
        }

        string classText;

        if (handler.HandlerType == HandlerType.StaticVerb)
        {
            var badges = new List<string> { "Static Verb" };
            var verbInfo = handler.VerbInfo;
            if (verbInfo is not null)
            {
                if (verbInfo.IsExtended) badges.Add("Shift-only");
                if (verbInfo.Position is not null) badges.Add(verbInfo.Position);
                if (verbInfo.HasLuaShield) badges.Add("UAC");
                if (verbInfo.IsProgrammaticAccessOnly) badges.Add("Hidden (Script-only)");
                if (verbInfo.DelegateExecuteClsid is not null) badges.Add("Delegated");
            }
            classText = string.Join(" | ", badges);
        }
        else if (handler.HandlerType == HandlerType.ModernPackaged)
        {
            var packagedInfo = handler.PackagedInfo;
            classText = packagedInfo is not null
                ? $"{packagedInfo.PackageDisplayName} -- {packagedInfo.PublisherDisplayName}"
                : handler.Publisher ?? "Unknown publisher";
        }
        else
        {
            classText = handler.Classification switch
            {
                HandlerClassification.Critical => "Windows built-in (critical)",
                HandlerClassification.System => $"Windows built-in -- {handler.Publisher ?? "Microsoft"}",
                HandlerClassification.Optional => $"Microsoft (optional) -- {handler.Publisher ?? "PowerToys"}",
                HandlerClassification.ThirdParty => handler.Publisher ?? "Unknown publisher",
                _ => handler.Publisher ?? string.Empty,
            };
        }

        if (!string.IsNullOrEmpty(scopeNote))
            classText += $" -- {scopeNote}";

        return classText;
    }

    private static string BuildWarningText(ContextMenuHandler handler)
    {
        if (handler.IsOrphaned)
            return "DLL missing — Explorer wastes resources trying to load this handler on every right-click";

        if (handler.IsContentInspecting)
            return "This handler performs synchronous file I/O during right-click -- may cause menu delays on large or network folders";

        return handler.Classification switch
        {
            HandlerClassification.Critical =>
                $"Disabling removes {handler.Name} from all right-click menus.",
            HandlerClassification.System =>
                "This is a Windows feature.",
            _ => string.Empty,
        };
    }

    [RelayCommand]
    private void CleanUpOrphan()
    {
        if (!IsOrphaned || _disposed || _orphanCleanupStaged)
            return;

        var cleanupGroup = ContextMenuChangeFactory.CreateOrphanCleanup(_handler);
        _pendingChangesService.Stage(cleanupGroup);
        _orphanCleanupStaged = true;
    }

    [RelayCommand]
    private void Migrate()
    {
        if (!CanMigrate || _disposed)
            return;

        var migrationGroup = ContextMenuChangeFactory.CreateMigration(_handler);
        _pendingChangesService.Stage(migrationGroup);
        CanMigrate = false;
    }

    partial void OnIsEnabledChanged(bool value)
    {
        if (_suppressStaging)
            return;

        // Modern packaged handlers cannot be toggled
        if (_handler.HandlerType == HandlerType.ModernPackaged)
            return;

        _debounceCts?.Cancel();
        _debounceCts?.Dispose();
        _debounceCts = new CancellationTokenSource();
        _ = DebounceToggleAsync(value, _debounceCts.Token);
    }

    private async Task DebounceToggleAsync(bool desiredState, CancellationToken token)
    {
        try
        {
            await Task.Delay(250, token).ConfigureAwait(true);
        }
        catch (TaskCanceledException)
        {
            return;
        }

        try
        {
            if (_disposed)
                return;

            // Refresh baseline from registry (source of truth)
            if (_readRegistryState is not null)
                _registryIsEnabled = _readRegistryState();

            // Route to appropriate change factory based on handler type
            List<ChangeDescriptor> allChanges;
            if (_handler.HandlerType == HandlerType.StaticVerb)
            {
                // Static verbs use LegacyDisable — single mechanism
                allChanges = [.. ContextMenuChangeFactory.CreateStaticVerbToggle(_handler, desiredState)];
            }
            else
            {
                // COM handlers: blocked list for universal coverage + dash-prefix for immediate Explorer effect
                var blockedListChange = ContextMenuChangeFactory.CreateBlockedListToggle(_handler, desiredState);
                var dashPrefixChanges = ContextMenuChangeFactory.CreateToggle(_handler, desiredState);
                allChanges = [blockedListChange, .. dashPrefixChanges];
            }

            _isStagingChange = true;
            try
            {
                // Unstage any existing pending change
                if (_stagedGroupId is not null)
                {
                    _pendingChangesService.Unstage(_stagedGroupId);
                    _stagedGroupId = null;
                }

                // Only stage if the desired state differs from the real registry value
                if (desiredState != _registryIsEnabled)
                {
                    var group = new ChangeGroup
                    {
                        GroupId = Guid.NewGuid().ToString("N"),
                        DisplayName = $"Context menu: {_handler.Name}",
                        Description = $"Toggle {_handler.Name} context menu handler",
                        Changes = allChanges,
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
            System.Diagnostics.Debug.WriteLine($"Toggle staging failed for {Label}: {ex.Message}");
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
                _registryIsEnabled = IsEnabled;
            }
            else
            {
                // Change was discarded — reset toggle to registry state
                _suppressStaging = true;
                IsEnabled = _registryIsEnabled;
                _suppressStaging = false;
            }

            UpdatePendingState();
        }
    }

    private void UpdatePendingState()
    {
        HasPendingChange = IsEnabled != _registryIsEnabled;
        IsPendingEnable = HasPendingChange && IsEnabled;
        IsPendingDisable = HasPendingChange && !IsEnabled;
    }

    public void Dispose()
    {
        _disposed = true;
        _pendingChangesService.PropertyChanged -= OnPendingChangesPropertyChanged;
        _debounceCts?.Cancel();
        _debounceCts?.Dispose();
        _debounceCts = null;
    }
}
