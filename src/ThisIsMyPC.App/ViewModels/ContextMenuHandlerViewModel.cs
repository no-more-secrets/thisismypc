using CommunityToolkit.Mvvm.ComponentModel;
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

    public HandlerClassification Classification { get; }
    public IReadOnlyList<string> AllScopes { get; }
    public string ScopeNote { get; private set; }
    public string Clsid { get; }
    public string? DllPath { get; }
    public IReadOnlyList<string> AllRegistryPaths { get; }
    public MiscSurfaceGroup? MiscGroup { get; set; }
    public string WarningText { get; }

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
        AllScopes = handler.AllScopes ?? [handler.AppliesTo];
        Clsid = handler.Clsid;
        DllPath = handler.DllPath;
        AllRegistryPaths = handler.AllRegistryPaths ?? [handler.RegistryPath];

        Label = handler.Name;
        ScopeNote = string.Empty; // Set after tab assignment via SetScopeNote
        Description = BuildDescription(handler);
        SystemPath = handler.RegistryPath;
        WarningText = BuildWarningText(handler);

        _suppressStaging = true;
        IsEnabled = handler.IsEnabled;
        _suppressStaging = false;
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
            Description = Clsid;
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
        var classText = handler.Classification switch
        {
            HandlerClassification.Critical => "Windows built-in (critical)",
            HandlerClassification.System => $"Windows built-in -- {handler.Publisher ?? "Microsoft"}",
            HandlerClassification.Optional => $"Microsoft (optional) -- {handler.Publisher ?? "PowerToys"}",
            HandlerClassification.ThirdParty => handler.Publisher ?? "Unknown publisher",
            _ => handler.Publisher ?? string.Empty,
        };

        if (!string.IsNullOrEmpty(scopeNote))
            classText += $" -- {scopeNote}";

        return classText;
    }

    private static string BuildWarningText(ContextMenuHandler handler) => handler.Classification switch
    {
        HandlerClassification.Critical =>
            $"Disabling removes {handler.Name} from all right-click menus. Explorer restart required.",
        HandlerClassification.System =>
            "This is a Windows feature. Explorer restart required.",
        _ => string.Empty,
    };

    partial void OnIsEnabledChanged(bool value)
    {
        if (_suppressStaging)
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
            // Refresh baseline from registry (source of truth)
            if (_readRegistryState is not null)
                _registryIsEnabled = _readRegistryState();

            var changes = ContextMenuChangeFactory.CreateToggle(_handler, desiredState);
            var settingId = ContextMenuChangeFactory.MakeSettingId(_handler.Clsid);

            // Unstage any existing pending change for this handler
            var existing = _pendingChangesService.PendingGroups
                .FirstOrDefault(g => g.Changes.Any(c => c.SettingId == settingId));
            if (existing is not null)
                _pendingChangesService.Unstage(existing.GroupId);

            // Only stage if the desired state differs from the real registry value
            if (desiredState != _registryIsEnabled)
            {
                var group = new ChangeGroup
                {
                    GroupId = Guid.NewGuid().ToString("N"),
                    DisplayName = $"Context menu: {_handler.Name}",
                    Description = $"Toggle {_handler.Name} context menu handler",
                    Changes = changes,
                };
                _pendingChangesService.Stage(group);
            }

            UpdatePendingState();
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Toggle staging failed for {Label}: {ex.Message}");
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
        _debounceCts?.Cancel();
        _debounceCts?.Dispose();
        _debounceCts = null;
    }
}
