using System.Collections.ObjectModel;
using System.ComponentModel;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ThisIsMyPC.Core.Services;
using ThisIsMyPC.Modules.Hardware.Cooling;

namespace ThisIsMyPC.App.ViewModels;

/// <summary>Edits a saved profile without changing the running FanControl configuration.</summary>
public sealed partial class CoolingProfilesViewModel : ViewModelBase, IDisposable
{
    private readonly FanControlProfileStore _store;
    private readonly IPendingChangesService _pending;
    private FanControlSavedProfile? _loaded;
    private int _loadEpoch;
    private bool _disposed;
    private bool _suppressSelectionLoad;
    private readonly Dictionary<string, EditorDraft> _drafts = new(StringComparer.OrdinalIgnoreCase);
    private sealed record EditorDraft(FanControlSavedProfile Profile, CoolingFanEditorViewModel[] Fans,
        CoolingCurveEditorViewModel[] Curves, string SaveName, bool ReplaceSelected);

    public CoolingProfilesViewModel(FanControlProfileStore store, IPendingChangesService pending, bool loadOnOpen = true)
    {
        _store = store;
        _pending = pending;
        _pending.PropertyChanged += OnPendingChanged;
        if (loadOnOpen) _ = RefreshAsync();
    }

    public ObservableCollection<string> Profiles { get; } = [];
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
        OnPropertyChanged(nameof(HasStagedProfile));
        OnPropertyChanged(nameof(StageLabel));
    }

    partial void OnSelectedProfileChanged(string? value)
    {
        if (value is not null && !_suppressSelectionLoad) _ = LoadAsync(value);
    }

    partial void OnReplaceSelectedChanged(bool value)
    {
        if (_loaded is not null)
            SaveName = value ? _loaded.Name : Path.GetFileNameWithoutExtension(_loaded.Name) + " - Edited.json";
    }

    partial void OnIsBusyChanged(bool value) => OnPropertyChanged(nameof(CanStage));
    partial void OnHasProfileChanged(bool value) => OnPropertyChanged(nameof(CanStage));

    [RelayCommand]
    private async Task RefreshAsync()
    {
        if (IsBusy) return;
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
        foreach (var name in result.Value!) Profiles.Add(name);
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
        Message = "Edits stay here until you stage and apply them. FanControl keeps its current settings.";
    }

    private void ClearEditor()
    {
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
                _pending.Stage(result.Value!);
                Message = "Profile save staged. Apply pending changes, then use Load configuration in FanControl to select the saved profile.";
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
        }
        if (Dispatcher.UIThread.CheckAccess()) Update();
        else Dispatcher.UIThread.Post(Update);
    }

    public void Dispose()
    {
        _disposed = true;
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
