using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ThisIsMyPC.App.Services;
using ThisIsMyPC.Core.Hardware;
using ThisIsMyPC.App.Icons;
using ThisIsMyPC.Core.Services;
using ThisIsMyPC.Core.Settings;

namespace ThisIsMyPC.App.ViewModels;

/// <summary>A desktop walkthrough. Lessons select recommendations, never mutate the PC.</summary>
public sealed partial class SetupGuideViewModel : ViewModelBase
{
    public const string PackName = "Desktop Grand Pack";
    private readonly IHardwareDetectionService? _hardware;
    private readonly Action<IReadOnlyList<string>> _review;
    private readonly Action<string> _openModule;
    private readonly ISettingsService? _settings;
    private const string ProgressScope = "Intro guide";
    public IReadOnlyList<SetupLessonViewModel> Lessons { get; }
    public IRelayCommand CloseCommand { get; }
    public GuideAutorunExampleViewModel? AutorunExample { get; }
    public bool HasLiveExample => IsDesktop && AutorunExample is not null && Current.IsAvailable
        && Current.ModuleName is "Software" or "Startup & Services";
    public bool IsWelcome => Current.ModuleName is null && SelectedIndex == 0;
    public bool HasSplitBody => IsWelcome || HasLiveExample;
    public bool HasLessonPlaceholder => Current.ModuleName is not null && Current.ModuleName is not ("Software" or "Startup & Services");
    public string Introduction => HasSplitBody ? Current.Body.Split("\n\n", 2)[0] : Current.Body;
    public string RemainingBody => HasSplitBody && Current.Body.Contains("\n\n", StringComparison.Ordinal)
        ? Current.Body[(Current.Body.IndexOf("\n\n", StringComparison.Ordinal) + 2)..] : "";

    [ObservableProperty] private int _selectedIndex;
    [ObservableProperty, NotifyPropertyChangedFor(nameof(HasLiveExample)), NotifyPropertyChangedFor(nameof(HasSplitBody)), NotifyPropertyChangedFor(nameof(Introduction)), NotifyPropertyChangedFor(nameof(RemainingBody))] private bool _isDesktop;
    [ObservableProperty, NotifyPropertyChangedFor(nameof(HasDeviceStatus))] private string _deviceStatus = "Checking desktop support…";
    public bool HasDeviceStatus => DeviceStatus.Length > 0;
    public SetupLessonViewModel Current => Lessons[SelectedIndex];
    public string Progress => $"{SelectedIndex + 1} of {Lessons.Count}";
    public bool HasPrevious => SelectedIndex > 0;
    public bool HasNext => SelectedIndex < Lessons.Count - 1;
    public bool IsLast => !HasNext;
    public bool CanOpenModule => Current.ModuleName is not null && Current.IsAvailable;

    public SetupGuideViewModel(IEnumerable<ModuleRegistration> modules, IHardwareDetectionService? hardware,
        Action<IReadOnlyList<string>> review, Action<string> openModule, Action close,
        IPendingChangesService? pending = null, IUserFeedback? feedback = null, AutorunEnrichment? enrichment = null,
        ISettingsService? settings = null)
    {
        _hardware = hardware;
        _review = review;
        _openModule = openModule;
        CloseCommand = new RelayCommand(close);
        var registrations = modules.ToArray();
        var startup = registrations.FirstOrDefault(m => m.Module.Info.Name == "Startup & Services" && m.Availability.IsAvailable);
        if (startup is not null && pending is not null)
            AutorunExample = new(startup.Module.ScanSystemStateAsync, pending, feedback, () => IsDesktop, enrichment);
        var lessons = new List<SetupLessonViewModel>
        {
            new("Welcome", "Welcome",
                "Windows hides things from you, puts promotions everywhere, lets installed apps fight over your PC, and does many things without giving you a choice. ThisIsMyPC exists to give you those choices back.\n\nHere, every control has been brought into one place. This tutorial will help you understand what Windows is doing to your personal computer, and what you can do about it with the help of this software (and others like it).\n\nTry out settings as you go through the tutorial. Examples are provided of what they will change. At the end, a preset called the Desktop Grand Pack has bundled many cleanup settings for your PC. You will be able to customize it thoroughly before applying, and the vast majority of changes are entirely reversible if you wish to go back.") { IconSymbol = FluentSymbol.Home }
        };
        string[] order = ["Software", "Startup & Services", "Windows Annoyances", "Privacy & Telemetry",
            "Explorer", "Context Menus", "Windows Update", "Security", "Network & Firewall", "Environment",
            "Power Plans", "Display", "System Control", "Lighting", "Cooling", "Monitoring"];
        foreach (var module in registrations.OrderBy(m =>
        {
            var index = Array.IndexOf(order, m.Module.Info.Name);
            return index < 0 ? int.MaxValue : index;
        }))
        {
            var lesson = SetupLessons.For(module.Module.Info.Name, module.Module.Info.Description);
            lesson.ModuleName = module.Module.Info.Name;
            lesson.IconSymbol = SidebarItemViewModel.GetIconSymbol(module.Module.Info.Icon);
            lesson.IsAvailable = module.Availability.IsAvailable;
            lesson.Availability = module.Availability.IsAvailable
                ? ""
                : module.Availability.Reason ?? "This module is unavailable on this PC.";
            lessons.Add(lesson);
        }
        lessons.Add(new("After cleanup", "Check what new software adds",
            "After installing an app, check Startup & Services and Context Menus for entries you did not ask for. After a major Windows update, check your promotion and privacy settings.\n\nClean Boot disables the Edge updater services. Restore both services before updating Edge. With Notify before download selected, check Windows Update regularly and remove the release pin before support ends.\n\nHistory can undo supported setting changes. Owner Mode can restore selected settings when they change; check its supported choices in Settings before enabling it.") { IconSymbol = FluentSymbol.History });
        lessons.Add(new("Your pack", "Review your desktop pack",
            "The pack includes Clean Boot, NukeCopilot, Privacy Baseline, and Windows Update Control, plus selected Windows 10-ify settings and app removals. You do not need to run those sets separately.\n\nChoose Review pack. Check each group's current values, clear anything you want to keep, then stage your selections. Entries unavailable on this PC are skipped.\n\nThe update settings require regular checks for updates and release support. App removals affect all users and have no undo.") { IconSymbol = FluentSymbol.Box });
        Lessons = lessons;
        Lessons[0].IsSelected = true;
        foreach (var lesson in Lessons.Where(l => l.HasRecommendations))
        {
            lesson.IncludeRecommendations = settings?.GetModule(ProgressScope, "include:" + lesson.ModuleName) != "0";
            lesson.PropertyChanged += (_, e) =>
            {
                if (e.PropertyName == nameof(SetupLessonViewModel.IncludeRecommendations))
                    SaveProgress("include:" + lesson.ModuleName, lesson.IncludeRecommendations ? "1" : "0");
            };
        }
        var savedLesson = settings?.GetModule(ProgressScope, "lesson");
        var savedIndex = lessons.FindIndex(l => (l.ModuleName ?? l.Label) == savedLesson);
        if (savedIndex >= 0) SelectedIndex = savedIndex;
        _settings = settings;
    }

    public async Task CheckDesktopAsync()
    {
        try
        {
            var snapshot = _hardware is null ? null : await _hardware.GetSnapshotAsync().ConfigureAwait(true);
            var factor = FormFactorClassifier.Classify(snapshot?.Facts.FormFactor).FormFactor;
            IsDesktop = factor == MachineFormFactor.Desktop;
            DeviceStatus = factor switch
            {
                MachineFormFactor.Desktop => "",
                MachineFormFactor.Laptop => "The cleanup pack requires a desktop PC.",
                _ => "PC type unknown. The cleanup pack is unavailable.",
            };
        }
        catch (Exception ex)
        {
            NLog.LogManager.GetCurrentClassLogger().Warn(ex, "Setup desktop check failed");
            IsDesktop = false;
            DeviceStatus = "Desktop check failed. Reopen the guide to retry.";
        }
    }

    partial void OnSelectedIndexChanged(int value)
    {
        for (var i = 0; i < Lessons.Count; i++) Lessons[i].IsSelected = i == value;
        foreach (var property in new[] { nameof(Current), nameof(Progress), nameof(HasPrevious), nameof(HasNext), nameof(IsLast), nameof(CanOpenModule), nameof(HasLiveExample), nameof(IsWelcome), nameof(HasSplitBody), nameof(HasLessonPlaceholder), nameof(Introduction), nameof(RemainingBody) })
            OnPropertyChanged(property);
        SaveProgress("lesson", Current.ModuleName ?? Current.Label);
    }

    private void SaveProgress(string key, string value)
    {
        try { _settings?.SetModule(ProgressScope, key, value); }
        catch (Exception ex) { NLog.LogManager.GetCurrentClassLogger().Warn(ex, "Guide progress could not be saved"); }
    }

    [RelayCommand] private void SelectLesson(SetupLessonViewModel lesson)
    {
        var index = Lessons.ToList().IndexOf(lesson);
        if (index >= 0) SelectedIndex = index;
    }

    [RelayCommand] private void Previous() { if (HasPrevious) SelectedIndex--; }
    [RelayCommand] private void Next() { if (HasNext) SelectedIndex++; }
    [RelayCommand] private void OpenModule() { if (CanOpenModule) _openModule(Current.ModuleName!); }
    [RelayCommand] private void ReviewPack()
    {
        if (IsDesktop) _review(Lessons.Where(l => l.HasRecommendations && !l.IncludeRecommendations)
            .Select(l => l.ModuleName!).ToArray());
    }
}

/// <summary>Plain language teaching and an optional recommendation choice for one module.</summary>
public sealed partial class SetupLessonViewModel(string label, string title, string body) : ViewModelBase
{
    public string Label { get; } = label;
    public string Title { get; } = title;
    public string Body { get; } = body;
    public FluentSymbol IconSymbol { get; set; } = FluentSymbol.Info;
    public string? ModuleName { get; set; }
    public bool IsAvailable { get; set; }
    public string Availability { get; set; } = "";
    public bool HasAvailability => Availability.Length > 0;
    [ObservableProperty] private bool _isSelected;
    public bool HasRecommendations => ModuleName is "Windows Annoyances" or "Privacy & Telemetry" or "Explorer" or "Software" or "Startup & Services" or "Windows Update";
    [ObservableProperty] private bool _includeRecommendations = true;
}
