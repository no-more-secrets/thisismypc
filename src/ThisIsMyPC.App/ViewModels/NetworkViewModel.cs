using System.Collections.ObjectModel;
using System.ComponentModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ThisIsMyPC.Core.Changes;
using ThisIsMyPC.Core.Network;
using ThisIsMyPC.Core.Services;
using ThisIsMyPC.Modules.Network;

namespace ThisIsMyPC.App.ViewModels;

public sealed partial class NetworkViewModel : ViewModelBase, ITabbedPage, ISearchNavigationTarget, IDisposable
{
    [ObservableProperty] private int _selectedTabIndex;
    [ObservableProperty] private string _ruleSearch = "";
    public IReadOnlyList<NetworkAdapterRow> Adapters { get; }
    public IReadOnlyList<NetworkSettingRow> Profiles { get; }
    public ObservableCollection<FirewallRuleState> Rules { get; } = [];
    private readonly IReadOnlyList<FirewallRuleState> _allRules;
    public string? AdapterError { get; }
    public string? FirewallError { get; }
    public bool HasAdapterError => AdapterError is not null;
    public bool HasFirewallError => FirewallError is not null;
    public bool NoAdapters => Adapters.Count == 0 && !HasAdapterError;
    public bool NoRules => Rules.Count == 0 && !HasFirewallError;
    public string RuleCount => $"{Rules.Count} of {_allRules.Count} rules";

    public NetworkViewModel(NetworkScanData data, IPendingChangesService pending, Services.IUserFeedback? feedback = null)
    {
        AdapterError = data.AdapterError;
        FirewallError = data.Firewall.Error;
        Adapters = data.Adapters.Select(a => new NetworkAdapterRow(a, pending, feedback)).ToArray();
        Profiles = data.Firewall.Profiles.Select(p => new NetworkSettingRow($"firewall/{p.Id}", p.Name,
            $"{(p.Active ? "Active profile" : "Inactive profile")} · Inbound: {p.Inbound} · Outbound: {p.Outbound}",
            p.Enabled ? "true" : "false", p.CanModify, false, pending, feedback,
            p.CanModify ? "Disabling removes this profile's Windows Firewall protection." : "Managed firewall settings cannot be changed here.")).ToArray();
        _allRules = data.Firewall.Rules;
        OnRuleSearchChanged("");
    }
    partial void OnRuleSearchChanged(string value)
    {
        Rules.Clear();
        foreach (var rule in _allRules.Where(r => string.Join(' ', r.Name, r.Application, r.Service, r.Direction, r.Action, r.Protocol, r.Profiles)
                     .Contains(value, StringComparison.OrdinalIgnoreCase))) Rules.Add(rule);
        OnPropertyChanged(nameof(RuleCount));
        OnPropertyChanged(nameof(NoRules));
    }
    public void NavigateToSearchResult(string settingId, string displayName) => SelectedTabIndex = settingId switch { "dns" => 1, "firewall" => 2, _ => 0 };
    public void Dispose()
    {
        foreach (var row in Adapters) { row.Connection.Dispose(); row.Dns.Dispose(); }
        foreach (var row in Profiles) row.Dispose();
    }
}

public sealed class NetworkAdapterRow
{
    public NetworkAdapterState State { get; }
    public NetworkSettingRow Connection { get; }
    public NetworkSettingRow Dns { get; }
    public string Summary => $"{(State.Kind == "Wireless80211" ? "Wi-Fi" : State.Kind)} · {State.LinkState switch { "Up" => "Connected", "Down" => "Disconnected", "NotPresent" => "Not present", _ => State.LinkState }}"
        + (State.LinkSpeed > 0 ? $" · {State.LinkSpeed / 1_000_000:N0} Mbps link" : "");
    public string Addresses => string.IsNullOrEmpty(State.Addresses) ? "No IP addresses" : "IP: " + State.Addresses;
    public string Gateways => string.IsNullOrEmpty(State.Gateways) ? "No gateway" : "Gateway: " + State.Gateways;
    public string EffectiveDns => string.IsNullOrEmpty(State.EffectiveDns) ? "No DNS servers reported" : "Reported DNS: " + State.EffectiveDns;
    public string Details => $"{State.Description}\nHardware address: {State.MacAddress}\n{Addresses}\n{Gateways}";
    public bool HasError => State.Error is not null;
    public NetworkAdapterRow(NetworkAdapterState state, IPendingChangesService pending, Services.IUserFeedback? feedback)
    {
        State = state;
        Connection = new($"adapter/{state.Id:D}", state.Name, Summary, state.Enabled == true ? "true" : "false",
            state.Enabled.HasValue, false, pending, feedback, "Disabling interrupts connections through this adapter.");
        Dns = new($"dns/{state.Id:D}", state.Name, EffectiveDns, state.StaticDns ?? NetworkChanges.Automatic,
            state.StaticDns is not null && NetworkChanges.TryDns(state.StaticDns, out _), true, pending, feedback,
            "IPv4 servers only. Leave blank for automatic configuration. IPv6 and encryption settings remain separate.");
    }
}

public sealed partial class NetworkSettingRow : ObservableObject, IDisposable
{
    private readonly string _target;
    private readonly IPendingChangesService _pending;
    private readonly Services.IUserFeedback? _feedback;
    private string _before;
    private string? _stagedAfter;
    private bool _synchronizing;
    public string Name { get; }
    public string Summary { get; }
    public string Hint { get; }
    public bool Supported { get; }
    public bool IsDns { get; }
    public bool CanEdit => Supported && !_pending.IsApplying && !IsStaged;
    public string Current => Supported ? NetworkChanges.Display(_before) : "Unavailable";
    [ObservableProperty] private bool _enabled;
    [ObservableProperty] private string _dnsText = "";
    [ObservableProperty] private bool _isStaged;
    [ObservableProperty] private string? _validationError;
    public bool IsInvalid => ValidationError is not null;
    public string GroupId => "network:" + _target;

    public NetworkSettingRow(string target, string name, string summary, string before, bool supported, bool isDns,
        IPendingChangesService pending, Services.IUserFeedback? feedback, string hint)
    {
        _target = target; Name = name; Summary = summary; _before = before; Supported = supported;
        IsDns = isDns; _pending = pending; _feedback = feedback; Hint = hint;
        _pending.PropertyChanged += PendingChanged;
        Synchronize();
    }
    partial void OnEnabledChanged(bool value) { if (!_synchronizing) Stage(value ? "true" : "false"); }
    partial void OnDnsTextChanged(string value) { ValidationError = null; OnPropertyChanged(nameof(IsInvalid)); }
    partial void OnIsStagedChanged(bool value) => OnPropertyChanged(nameof(CanEdit));
    [RelayCommand] private void SaveDns()
    {
        if (!NetworkChanges.TryDns(DnsText, out var after))
        {
            ValidationError = "Enter up to eight IPv4 addresses, separated by spaces or commas.";
            OnPropertyChanged(nameof(IsInvalid));
            _feedback?.Fail(ValidationError);
            return;
        }
        Stage(after);
    }
    [RelayCommand] private void Discard() { if (!_pending.IsApplying) _pending.Unstage(GroupId); }
    private void Stage(string after)
    {
        if (!CanEdit) { Synchronize(); return; }
        if (after == _before) return;
        try
        {
            var change = NetworkChanges.Create(_target, Name + (IsDns ? " DNS" : _target.StartsWith("firewall/", StringComparison.Ordinal) ? " firewall" : " adapter"), _before, after);
            _pending.Stage(new ChangeGroup { GroupId = GroupId, DisplayName = change.DisplayName, Description = Hint, Changes = [change] });
            Synchronize();
        }
        catch (Exception ex) { _feedback?.Fail(ex.Message); Synchronize(); }
    }
    private void PendingChanged(object? sender, PropertyChangedEventArgs e)
    {
        void Update()
        {
            if (IsStaged || _pending.PendingGroups.Any(g => g.GroupId == GroupId)) Synchronize();
            OnPropertyChanged(nameof(CanEdit));
        }
        if (Avalonia.Threading.Dispatcher.UIThread.CheckAccess()) Update();
        else Avalonia.Threading.Dispatcher.UIThread.Post(Update);
    }
    private void Synchronize()
    {
        _synchronizing = true;
        try
        {
            var group = _pending.PendingGroups.FirstOrDefault(g => g.GroupId == GroupId);
            if (group is null && _stagedAfter is not null && _pending.WasApplied(GroupId)) _before = _stagedAfter;
            _stagedAfter = group?.Changes[0].AfterValue;
            IsStaged = group is not null;
            var value = _stagedAfter ?? _before;
            Enabled = value == "true";
            DnsText = value == NetworkChanges.Automatic ? "" : value;
            OnPropertyChanged(nameof(Current));
            OnPropertyChanged(nameof(CanEdit));
        }
        finally { _synchronizing = false; }
    }
    public void Dispose() => _pending.PropertyChanged -= PendingChanged;
}
