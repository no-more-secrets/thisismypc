using System.ComponentModel;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using ThisIsMyPC.Core.Policies;
using ThisIsMyPC.Core.Services;
using ThisIsMyPC.Modules.Security;

namespace ThisIsMyPC.App.ViewModels;

public sealed partial class SecurityViewModel : ViewModelBase, ITabbedPage, ISearchNavigationTarget, IDisposable
{
    private readonly IPendingChangesService _pending;
    private bool _disposed;
    public IReadOnlyList<SecuritySectionViewModel> Sections { get; }
    public IReadOnlyList<SecurityRowViewModel> Rows { get; }
    public bool IsSearching => !string.IsNullOrWhiteSpace(SearchText);
    [ObservableProperty] private int _selectedTabIndex;
    [ObservableProperty] private string _searchText = "";

    public SecurityViewModel(IRegistryService registry, IPendingChangesService pending, ICapabilityDetector? capabilities = null,
        PolicyControlStateReader? policies = null, Services.IUserFeedback? feedback = null)
    {
        _pending = pending;
        var reader = new SecuritySettings(registry, policies);
        Rows = SecurityCatalog.Settings.Select(s => new SecurityRowViewModel(s, reader, pending, capabilities, feedback)).ToList();
        Sections = Rows.GroupBy(r => r.Setting.Section).Select(g => new SecuritySectionViewModel(g.Key, g.ToList())).ToList();
        pending.PropertyChanged += PendingChanged;
    }

    partial void OnSearchTextChanged(string value)
    {
        foreach (var row in Rows) row.IsVisible = string.IsNullOrWhiteSpace(value)
            || row.Setting.Title.Contains(value, StringComparison.OrdinalIgnoreCase)
            || row.Setting.Description.Contains(value, StringComparison.OrdinalIgnoreCase)
            || row.Setting.Targets.Any(t => t.Name.Contains(value, StringComparison.OrdinalIgnoreCase));
        OnPropertyChanged(nameof(IsSearching));
    }
    public void NavigateToSearchResult(string settingId, string displayName) => SearchText = displayName;
    private void PendingChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (_disposed || e.PropertyName is not (nameof(IPendingChangesService.PendingGroups) or nameof(IPendingChangesService.IsApplying)
            or nameof(IPendingChangesService.ReconciliationRequired))) return;
        var readLive = e.PropertyName == nameof(IPendingChangesService.IsApplying) && !_pending.IsApplying;
        if (!Dispatcher.UIThread.CheckAccess()) { Dispatcher.UIThread.Post(() => RefreshRows(readLive)); return; }
        RefreshRows(readLive);
    }
    private void RefreshRows(bool readLive)
    {
        if (_disposed) return;
        foreach (var row in Rows) row.Refresh(readLive);
    }
    public void Dispose()
    {
        _disposed = true;
        _pending.PropertyChanged -= PendingChanged;
    }
}

public sealed record SecuritySectionViewModel(string Header, IReadOnlyList<SecurityRowViewModel> Rows);

public sealed partial class SecurityRowViewModel : ObservableObject
{
    private readonly SecuritySettings _reader;
    private readonly IPendingChangesService _pending;
    private readonly Services.IUserFeedback? _feedback;
    private bool _changing;
    private string? _groupId;
    private string? _blocked;
    private SecurityOption? _displayed;
    private SecuritySnapshot _snapshot;
    public SecuritySetting Setting { get; }
    public IReadOnlyList<SecurityOption> Choices => Setting.Choices;
    public string EditionNotice { get; }
    public bool IsControlEnabled => _blocked is null && !_pending.IsApplying && _pending.ReconciliationRequired.Count == 0;
    [ObservableProperty] private SecurityOption? _selectedOption;
    [ObservableProperty] private string? _policySummary;
    [ObservableProperty] private string? _policyText;
    [ObservableProperty] private bool _hasPendingChange;
    [ObservableProperty] private bool _isVisible = true;
    private readonly string? _editionBlock;

    public SecurityRowViewModel(SecuritySetting setting, SecuritySettings reader, IPendingChangesService pending,
        ICapabilityDetector? capabilities, Services.IUserFeedback? feedback)
    {
        Setting = setting; _reader = reader; _pending = pending; _feedback = feedback;
        _editionBlock = SettingEditionSupport.BlockReason(capabilities?.Sku, setting.Edition);
        EditionNotice = SettingEditionSupport.RequirementLabel(setting.Edition)!;
        _snapshot = reader.Read(setting);
        Refresh();
    }

    public void Refresh(bool readLive = false)
    {
        if (_changing) return;
        _changing = true;
        try
        {
            // Staging and discarding only change the queue. Keep the displayed
            // snapshot until Apply completes or the page is reloaded. Create and
            // Stage still independently validate fresh state before accepting a choice.
            if (readLive) _snapshot = _reader.Read(Setting);
            var state = _snapshot;
            _blocked = _editionBlock ?? state.BlockReason;
            PolicyText = state.BlockReason;
            PolicySummary = state.BlockReason is null ? null
                : state.BlockReason.StartsWith("Controlled by saved local policy.", StringComparison.Ordinal)
                    ? "Managed by local policy" : "Policy state needs review";
            var existing = _pending.PendingGroups.FirstOrDefault(g => g.Changes.Any(c => c.ModuleId == SecurityCatalog.ModuleId && c.SettingId == Setting.Id));
            _groupId = existing?.GroupId;
            var values = state.Values.ToArray();
            if (existing is not null)
                for (var i = 0; i < values.Length; i++)
                    values[i] = existing.Changes.FirstOrDefault(c => c.SystemLocation == Setting.Targets[i].Location)?.AfterValue ?? values[i];
            SelectedOption = existing is null ? state.Option : Choices.FirstOrDefault(o => o.Values.SequenceEqual(values));
            _displayed = SelectedOption;
            HasPendingChange = existing is not null;
            OnPropertyChanged(nameof(IsControlEnabled));
        }
        finally { _changing = false; }
    }

    partial void OnSelectedOptionChanged(SecurityOption? value)
    {
        if (_changing || value is null) return;
        if (!IsControlEnabled) { _changing = true; SelectedOption = _displayed; _changing = false; return; }
        _changing = true;
        try
        {
            var group = _reader.Create(Setting, value);
            // Validate and stage the replacement before removing the prior choice.
            if (group.Changes.Any(change => change.BeforeValue != change.AfterValue)) _pending.Stage(group);
            if (_groupId is not null) _pending.Unstage(_groupId);
        }
        catch (Exception ex) when (ex is InvalidOperationException or ArgumentException)
        { _feedback?.Fail(ex.Message); }
        finally { _changing = false; Refresh(); }
    }
}
