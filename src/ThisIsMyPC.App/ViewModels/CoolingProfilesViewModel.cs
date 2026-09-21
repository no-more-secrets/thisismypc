using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Collections.Specialized;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ThisIsMyPC.Core.Services;
using ThisIsMyPC.Core.Results;
using ThisIsMyPC.Modules.Hardware.Cooling;

namespace ThisIsMyPC.App.ViewModels;

/// <summary>Edits saved profiles and separately requests a FanControl switch with user confirmation.</summary>
public sealed partial class CoolingProfilesViewModel : ViewModelBase, IDisposable
{
    private readonly FanControlProfileStore _store;
    private readonly IPendingChangesService _pending;
    private readonly Func<string, Task<OperationResult<bool>>>? _requestActivation;
    private readonly Dictionary<string, (string Name, byte[] Bytes)> _stagedSaves = [];
    private readonly HashSet<INotifyPropertyChanged> _editorSubscriptions = [];
    private readonly DispatcherTimer _activationTimer = new() { Interval = TimeSpan.FromSeconds(1) };
    private byte[]? _requestedBytes;
    private int _activationEpoch;
    private bool _validatingActivation;
    private FanControlSavedProfile? _loaded;
    private int _loadEpoch;
    private bool _disposed;
    private bool _suppressSelectionLoad;
    private readonly Dictionary<string, EditorDraft> _drafts = new(StringComparer.OrdinalIgnoreCase);
    private sealed record EditorDraft(FanControlSavedProfile Profile, CoolingFanEditorViewModel[] Fans,
        CoolingCurveEditorViewModel[] Curves, string SaveName, bool ReplaceSelected);

    public CoolingProfilesViewModel(FanControlProfileStore store, IPendingChangesService pending, bool loadOnOpen = true,
        Func<string, Task<OperationResult<bool>>>? requestActivation = null)
    {
        _store = store;
        _pending = pending;
        _requestActivation = requestActivation;
        _activationTimer.Tick += OnActivationTick;
        _pending.PropertyChanged += OnPendingChanged;
        if (loadOnOpen) _ = RefreshAsync();
    }

    public ObservableCollection<string> Profiles { get; } = [];
    public ObservableCollection<string> ActivationProfiles { get; } = [];
    [ObservableProperty] private string? _activationProfile;
    [ObservableProperty] private string _activationStatus = "No profile switch requested.";
    [ObservableProperty] private bool _activationRequested;
    [ObservableProperty] private bool _confirmedByUser;
    [ObservableProperty] private string? _confirmationDetail;
    private bool HasPendingProfileSave => _pending.PendingGroups.Any(group => group.Changes.Any(FanControlProfileStore.IsProfileChange));
    public bool CanRequestActivation => !_disposed && !IsBusy && !_pending.IsApplying && !HasPendingProfileSave
        && _requestActivation is not null && ActivationProfile is not null;
    public bool CanConfirmActivation => CanRequestActivation && ActivationRequested && !ConfirmedByUser;

    partial void OnActivationProfileChanged(string? value) => InvalidateActivation();
    partial void OnActivationRequestedChanged(bool value) => NotifyActivation();
    partial void OnConfirmedByUserChanged(bool value) => NotifyActivation();
    private void NotifyActivation()
    {
        OnPropertyChanged(nameof(CanRequestActivation));
        OnPropertyChanged(nameof(CanConfirmActivation));
    }

    private void InvalidateActivation(string message = "No profile switch requested.")
    {
        _activationEpoch++;
        _requestedBytes = null;
        ActivationRequested = false;
        ConfirmedByUser = false;
        ConfirmationDetail = null;
        ActivationStatus = message;
        _activationTimer.Stop();
        NotifyActivation();
    }

    [RelayCommand]
    private async Task RequestActivationAsync()
    {
        if (!CanRequestActivation) return;
        InvalidateActivation();
        var epoch = _activationEpoch;
        var name = ActivationProfile!;
        IsBusy = true;
        try
        {
            var saved = await _store.ReadAsync(name);
            if (_disposed || epoch != _activationEpoch) return;
            if (!saved.IsSuccess) { InvalidateActivation(saved.ErrorMessage ?? "Saved profile is unavailable."); return; }
            var result = await _requestActivation!(name);
            if (_disposed || epoch != _activationEpoch) return;
            if (!result.IsSuccess || !result.Value)
            {
                InvalidateActivation(result.ErrorMessage ?? "FanControl did not accept the request. Load the saved configuration in FanControl.");
                return;
            }
            _requestedBytes = saved.Value!.Bytes.ToArray();
            ActivationRequested = true;
            if (!await ValidateActivationAsync()) return;
            ActivationStatus = "Switch requested. Check the loaded curves in FanControl, then confirm below.";
            _activationTimer.Start();
        }
        catch (OperationCanceledException) { if (epoch == _activationEpoch) InvalidateActivation("Profile switch request canceled."); }
        catch (Exception ex) { if (epoch == _activationEpoch) InvalidateActivation("Profile switch request failed: " + ex.Message); }
        finally { IsBusy = false; }
    }

    public async Task<bool> ValidateActivationAsync()
    {
        if (!ActivationRequested || _requestedBytes is null || ActivationProfile is null || _disposed) return false;
        var epoch = _activationEpoch;
        var result = await _store.ReadAsync(ActivationProfile);
        if (_disposed || epoch != _activationEpoch) return false;
        if (!result.IsSuccess || !result.Value!.Bytes.AsSpan().SequenceEqual(_requestedBytes))
        {
            InvalidateActivation("The saved profile changed or is unavailable. Request the switch again and check the curves.");
            return false;
        }
        return true;
    }

    [RelayCommand]
    private async Task ConfirmActivationAsync()
    {
        if (!CanConfirmActivation) return;
        if (!await ValidateActivationAsync() || !CanConfirmActivation) return;
        ConfirmedByUser = true;
        ActivationStatus = "Confirmed by you";
        ConfirmationDetail = $"Your check at {DateTime.Now:t}. ThisIsMyPC cannot detect later profile switches in FanControl.";
    }

    [RelayCommand]
    private void RejectActivation() => InvalidateActivation("Not loaded. In FanControl, use Load configuration to choose the saved profile, then request and check again.");

    private async void OnActivationTick(object? sender, EventArgs e)
    {
        if (_validatingActivation) return;
        _validatingActivation = true;
        try { await ValidateActivationAsync(); }
        finally { _validatingActivation = false; }
    }

    private void OnEditorChanged(object? sender, PropertyChangedEventArgs e) => InvalidateActivation("Editor changed. Save and apply edits before requesting the saved profile.");
    private void OnPointsChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        OnEditorChanged(sender, new(null));
        AttachEditorEvents();
    }
    private void AttachEditorEvents()
    {
        foreach (var curve in Curves)
        {
            curve.Points.CollectionChanged -= OnPointsChanged;
            curve.Points.CollectionChanged += OnPointsChanged;
        }
        foreach (var item in Fans.Cast<INotifyPropertyChanged>().Concat(Curves).Concat(Curves.SelectMany(curve => curve.Points)))
            if (_editorSubscriptions.Add(item)) item.PropertyChanged += OnEditorChanged;
    }
    private void DetachEditorEvents()
    {
        foreach (var item in _editorSubscriptions) item.PropertyChanged -= OnEditorChanged;
        _editorSubscriptions.Clear();
        foreach (var curve in Curves) curve.Points.CollectionChanged -= OnPointsChanged;
    }
    public ObservableCollection<CoolingFanEditorViewModel> Fans { get; } = [];
    public ObservableCollection<CoolingCurveEditorViewModel> Curves { get; } = [];
    [ObservableProperty] private string? _selectedProfile;
    [ObservableProperty] private string _saveName = "";
    [ObservableProperty] private bool _replaceSelected;
    [ObservableProperty] private bool _isBusy;
    [ObservableProperty] private bool _hasProfile;
    [ObservableProperty] private string? _message;
    [ObservableProperty] private bool _failed;
    [ObservableProperty] private CoolingFanEditorViewModel? _selectedFan;
    [ObservableProperty] private CoolingCurveEditorViewModel? _selectedCurve;

    public bool CanStage => HasProfile && !IsBusy && !_pending.IsApplying;
    public bool HasStagedProfile => _pending.PendingGroups.Any(group => group.Changes.Any(change => FanControlProfileStore.IsProfileChange(change) && string.Equals(Path.GetFileName(change.SystemLocation), SaveName.Trim(), StringComparison.OrdinalIgnoreCase)));

    public string StageLabel => HasStagedProfile ? "Update staged save" : "Stage profile save";

    partial void OnSaveNameChanged(string value)
    {
        InvalidateActivation();
        OnPropertyChanged(nameof(HasStagedProfile));
        OnPropertyChanged(nameof(StageLabel));
    }

    partial void OnSelectedProfileChanged(string? value)
    {
        InvalidateActivation();
        ActivationProfile = value;
        if (value is not null && !_suppressSelectionLoad) _ = LoadAsync(value);
    }

    partial void OnReplaceSelectedChanged(bool value)
    {
        InvalidateActivation();
        if (_loaded is not null)
            SaveName = value ? _loaded.Name : Path.GetFileNameWithoutExtension(_loaded.Name) + " - Edited.json";
    }

    partial void OnIsBusyChanged(bool value) { OnPropertyChanged(nameof(CanStage)); NotifyActivation(); }
    partial void OnHasProfileChanged(bool value) => OnPropertyChanged(nameof(CanStage));

    [RelayCommand]
    private async Task RefreshAsync()
    {
        if (IsBusy) return;
        InvalidateActivation();
        IsBusy = true;
        var result = await _store.ListAsync();
        if (_disposed) return;
        IsBusy = false;
        if (!result.IsSuccess)
        {
            Failed = true;
            Message = result.ErrorMessage;
            return;
        }
        var selection = SelectedProfile;
        _drafts.Clear();
        _loaded = null;
        _suppressSelectionLoad = true;
        Profiles.Clear();
        ActivationProfiles.Clear();
        foreach (var name in result.Value!) { Profiles.Add(name); ActivationProfiles.Add(name); }
        SelectedProfile = selection is not null && Profiles.Contains(selection) ? selection : Profiles.FirstOrDefault();
        _suppressSelectionLoad = false;
        if (SelectedProfile is not null) await LoadAsync(SelectedProfile);
        if (Profiles.Count == 0)
        {
            ClearEditor();
            Message = "Save a configuration in FanControl first. Then reload profiles here.";
            Failed = false;
        }
    }

    private async Task LoadAsync(string name)
    {
        var epoch = ++_loadEpoch;
        IsBusy = true;
        if (_loaded is not null)
            _drafts[_loaded.Name] = new(_loaded, Fans.ToArray(), Curves.ToArray(), SaveName, ReplaceSelected);
        ClearEditor();
        if (_drafts.TryGetValue(name, out var draft))
        {
            _loaded = draft.Profile;
            foreach (var fan in draft.Fans) Fans.Add(fan);
            foreach (var curve in draft.Curves) Curves.Add(curve);
            SelectedFan = Fans.FirstOrDefault();
            SelectedCurve = Curves.FirstOrDefault();
            ReplaceSelected = draft.ReplaceSelected;
            SaveName = draft.SaveName;
            HasProfile = true;
            AttachEditorEvents();
            IsBusy = false;
            Failed = false;
            Message = "Your edits are kept while you switch profiles. Reload profiles discards editor changes.";
            return;
        }
        var result = await _store.ReadAsync(name);
        if (_disposed || epoch != _loadEpoch) return;
        IsBusy = false;
        Failed = !result.IsSuccess;
        if (!result.IsSuccess)
        {
            Message = result.ErrorMessage;
            return;
        }
        _loaded = result.Value!;
        foreach (var curve in _loaded.Document.Curves) Curves.Add(new(curve));
        var choices = new[] { new CoolingCurveChoice(null, "No assigned curve") }
            .Concat(_loaded.Document.Curves.Select(curve => new CoolingCurveChoice(curve.Name, curve.Name))).ToArray();
        foreach (var fan in _loaded.Document.Controls) Fans.Add(new(fan, choices));
        SelectedFan = Fans.FirstOrDefault();
        SelectedCurve = Curves.FirstOrDefault();
        ReplaceSelected = false;
        SaveName = Path.GetFileNameWithoutExtension(name) + " - Edited.json";
        HasProfile = true;
        AttachEditorEvents();
        Message = "Edits stay here until you stage and apply them. FanControl keeps its current settings.";
    }

    private void ClearEditor()
    {
        DetachEditorEvents();
        HasProfile = false;
        _loaded = null;
        SelectedFan = null;
        SelectedCurve = null;
        Fans.Clear();
        Curves.Clear();
    }

    [RelayCommand]
    private async Task StageAsync()
    {
        if (!CanStage || _loaded is null) return;
        IsBusy = true;
        try
        {
            foreach (var fan in Fans) fan.CopyToModel();
            foreach (var curve in Curves) curve.CopyToModel();
            var result = await _store.BuildChangeAsync(_loaded, SaveName.Trim(), ReplaceSelected);
            if (_disposed) return;
            Failed = !result.IsSuccess;
            if (result.IsSuccess)
            {
                _stagedSaves[result.Value!.GroupId] = (SaveName.Trim(), Convert.FromBase64String(result.Value.Changes.Single().AfterValue!));
                _pending.Stage(result.Value!);
                Message = "Profile save staged. Apply pending changes, then request the saved profile switch below.";
            }
            else Message = result.ErrorMessage;
        }
        finally { IsBusy = false; }
    }

    private void OnPendingChanged(object? sender, PropertyChangedEventArgs e)
    {
        void Update()
        {
            if (_disposed) return;
            OnPropertyChanged(nameof(HasStagedProfile));
            OnPropertyChanged(nameof(StageLabel));
            OnPropertyChanged(nameof(CanStage));
            if (HasPendingProfileSave || _pending.IsApplying) InvalidateActivation("Apply or discard pending profile saves before requesting a switch.");
            foreach (var entry in _stagedSaves.ToArray())
            {
                if (_pending.PendingGroups.Any(group => group.GroupId == entry.Key)) continue;
                if (_pending.WasApplied(entry.Key))
                {
                    _ = SelectAppliedProfileAsync(entry.Value.Name, entry.Value.Bytes);
                }
                _stagedSaves.Remove(entry.Key);
            }
            NotifyActivation();
        }
        if (Dispatcher.UIThread.CheckAccess()) Update();
        else Dispatcher.UIThread.Post(Update);
    }

    private async Task SelectAppliedProfileAsync(string name, byte[] expectedBytes)
    {
        var result = await _store.ReadAsync(name);
        if (_disposed || !result.IsSuccess || !result.Value!.Bytes.AsSpan().SequenceEqual(expectedBytes)) return;
        if (!ActivationProfiles.Contains(name)) ActivationProfiles.Add(name);
        ActivationProfile = name;
    }

    public void Dispose()
    {
        _disposed = true;
        InvalidateActivation();
        _activationTimer.Tick -= OnActivationTick;
        DetachEditorEvents();
        _loadEpoch++;
        _pending.PropertyChanged -= OnPendingChanged;
    }
}

public sealed record CoolingCurveChoice(string? Name, string Label);

public sealed partial class CoolingFanEditorViewModel : ViewModelBase
{
    private readonly FanControlProfileControl _model;
    public CoolingFanEditorViewModel(FanControlProfileControl model, IReadOnlyList<CoolingCurveChoice> curveChoices)
    {
        _model = model;
        _nickname = model.NickName ?? "";
        _enabled = model.Enabled;
        _manualControl = model.ManualControl;
        _manualPercent = model.ManualPercent;
        _selectedCurve = curveChoices.First(choice => choice.Name == model.CurveName);
        CurveChoices = curveChoices;
    }
    public string Name => _model.Name;
    public string Label => string.IsNullOrEmpty(_model.NickName) ? Name : _model.NickName;
    public IReadOnlyList<CoolingCurveChoice> CurveChoices { get; }
    [ObservableProperty] private string _nickname;
    [ObservableProperty] private bool _enabled;
    [ObservableProperty] private bool _manualControl;
    [ObservableProperty] private decimal _manualPercent;
    [ObservableProperty] private CoolingCurveChoice _selectedCurve;
    public void CopyToModel()
    {
        if (Nickname != (_model.NickName ?? "")) _model.NickName = Nickname;
        _model.Enabled = Enabled;
        _model.ManualControl = ManualControl;
        _model.ManualPercent = ManualPercent;
        _model.CurveName = SelectedCurve.Name;
    }
}

public sealed partial class CoolingCurveEditorViewModel : ViewModelBase
{
    private readonly FanControlProfileCurve _model;
    public CoolingCurveEditorViewModel(FanControlProfileCurve model)
    {
        _model = model;
        _percent = model.Percent;
        foreach (var point in model.Points) Points.Add(new(point.Temperature, point.Percent));
    }
    public string Name => _model.Name;
    public bool IsFlat => _model.Kind == FanControlCurveKind.Flat;
    public bool IsGraph => _model.Kind == FanControlCurveKind.Graph;
    public bool IsUnsupported => _model.Kind == FanControlCurveKind.Unsupported;
    public string? TemperatureSource => _model.TemperatureSource;
    public ObservableCollection<CoolingPointEditorViewModel> Points { get; } = [];
    [ObservableProperty] private decimal _percent;
    [RelayCommand] private void AddPoint()
    {
        if (Points.Count >= 100) return;
        if (Points.Count < 2) return;
        var left = Points[^2];
        var right = Points[^1];
        var temperature = (left.Temperature + right.Temperature) / 2;
        if (temperature <= left.Temperature || temperature >= right.Temperature) return;
        Points.Insert(Points.Count - 1, new(temperature, (left.Percent + right.Percent) / 2));
    }
    [RelayCommand] private void RemovePoint(CoolingPointEditorViewModel point)
    {
        if (Points.Count > 2) Points.Remove(point);
    }
    public void CopyToModel()
    {
        _model.Percent = Percent;
        _model.Points.Clear();
        _model.Points.AddRange(Points.Select(point => new FanControlCurvePoint(point.Temperature, point.Percent)));
    }
}

public sealed partial class CoolingPointEditorViewModel(decimal temperature, decimal percent) : ViewModelBase
{
    [ObservableProperty] private decimal _temperature = temperature;
    [ObservableProperty] private decimal _percent = percent;
}
