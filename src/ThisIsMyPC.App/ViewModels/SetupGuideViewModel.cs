using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ThisIsMyPC.App.Services;
using ThisIsMyPC.Core.Hardware;

namespace ThisIsMyPC.App.ViewModels;

/// <summary>A desktop walkthrough. Lessons select recommendations, never mutate the PC.</summary>
public sealed partial class SetupGuideViewModel : ViewModelBase
{
    public const string PackName = "Desktop Grand Pack";
    private readonly IHardwareDetectionService? _hardware;
    private readonly Action<IReadOnlyList<string>> _review;
    private readonly Action<string> _openModule;
    public IReadOnlyList<SetupLessonViewModel> Lessons { get; }
    public IRelayCommand CloseCommand { get; }

    [ObservableProperty] private int _selectedIndex;
    [ObservableProperty] private bool _isDesktop;
    [ObservableProperty, NotifyPropertyChangedFor(nameof(HasDeviceStatus))] private string _deviceStatus = "Checking desktop support…";
    public bool HasDeviceStatus => DeviceStatus.Length > 0;
    public SetupLessonViewModel Current => Lessons[SelectedIndex];
    public string Progress => $"{SelectedIndex + 1} of {Lessons.Count}";
    public bool HasPrevious => SelectedIndex > 0;
    public bool HasNext => SelectedIndex < Lessons.Count - 1;
    public bool IsLast => !HasNext;
    public bool CanOpenModule => Current.ModuleName is not null && Current.IsAvailable;

    public SetupGuideViewModel(IEnumerable<ModuleRegistration> modules, IHardwareDetectionService? hardware,
        Action<IReadOnlyList<string>> review, Action<string> openModule, Action close)
    {
        _hardware = hardware;
        _review = review;
        _openModule = openModule;
        CloseCommand = new RelayCommand(close);
        var lessons = new List<SetupLessonViewModel>
        {
            new("Start here", "Remove unwanted apps and Windows promotions",
                "Start with apps you do not use, programs that launch at sign-in, and Windows promotions. This guide explains where to change each of them.\n\nIt also covers the other modules, so you can find settings for your files, hardware, and network. At the end, the Desktop Grand Pack brings the recommended cleanup changes together.")
        };
        string[] order = ["Software", "Startup & Services", "Windows Annoyances", "Privacy & Telemetry",
            "Explorer", "Context Menus", "Windows Update", "Security", "Network & Firewall", "Environment",
            "Power Plans", "Display", "System Control", "Lighting", "Cooling", "Monitoring"];
        foreach (var module in modules.OrderBy(m =>
        {
            var index = Array.IndexOf(order, m.Module.Info.Name);
            return index < 0 ? int.MaxValue : index;
        }))
        {
            var lesson = SetupLessons.For(module.Module.Info.Name, module.Module.Info.Description);
            lesson.ModuleName = module.Module.Info.Name;
            lesson.IsAvailable = module.Availability.IsAvailable;
            lesson.Availability = module.Availability.IsAvailable
                ? ""
                : module.Availability.Reason ?? "This module is unavailable on this PC.";
            lessons.Add(lesson);
        }
        lessons.Add(new("After cleanup", "Check what new software adds",
            "After installing an app, check Startup & Services and Context Menus for entries you did not ask for. After a major Windows update, check your promotion and privacy settings.\n\nHistory can undo supported setting changes. Owner Mode can restore selected settings when they change; check its supported choices in Settings before enabling it."));
        lessons.Add(new("Your pack", "Review your desktop pack",
            "The pack combines promotion, privacy, Copilot, and Widgets settings with five optional app removals. Modules you excluded stay unchecked.\n\nChoose Review pack to see each setting and installed app. Clear anything you want to keep, then stage the remaining selections. App removals affect all users and have no undo."));
        Lessons = lessons;
        Lessons[0].IsSelected = true;
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
        foreach (var property in new[] { nameof(Current), nameof(Progress), nameof(HasPrevious), nameof(HasNext), nameof(IsLast), nameof(CanOpenModule) })
            OnPropertyChanged(property);
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
    public string? ModuleName { get; set; }
    public bool IsAvailable { get; set; }
    public string Availability { get; set; } = "";
    public bool HasAvailability => Availability.Length > 0;
    [ObservableProperty] private bool _isSelected;
    public bool HasRecommendations => ModuleName is "Windows Annoyances" or "Privacy & Telemetry" or "Explorer" or "Software";
    [ObservableProperty] private bool _includeRecommendations = true;
}
