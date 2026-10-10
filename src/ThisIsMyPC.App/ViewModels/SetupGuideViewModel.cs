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
    [ObservableProperty] private string _deviceStatus = "Checking desktop support…";
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
            new("Start here", "Make this PC yours",
                "Windows, the PC manufacturer, and installed apps all make choices about your PC. This guide helps you review those choices.",
                "Promotions, bundled apps, and background activity can serve their publisher's goals rather than yours. Some also provide features you need.",
                "Visit each module, learn the tradeoffs, and choose recommendations. Nothing changes until you review, stage, and apply the pack.",
                "You can leave and return from Home. Settings can support undo; removing an app requires a separate reinstall.")
        };
        foreach (var module in modules)
        {
            var lesson = SetupLessons.For(module.Module.Info.Name, module.Module.Info.Description);
            lesson.ModuleName = module.Module.Info.Name;
            lesson.IsAvailable = module.Availability.IsAvailable;
            lesson.Availability = module.Availability.IsAvailable
                ? "Module available. Open it to inspect this PC's current settings."
                : module.Availability.Reason ?? "This module is unavailable on this PC.";
            lessons.Add(lesson);
        }
        lessons.Add(new("Keep control", "Keep your choices",
            "A cleanup is a starting point. Check your choices after installing software or making a major Windows update.",
            "Apps can add startup entries or menu items. Defaults and available controls can change. A changed setting alone does not prove the cause.",
            "Use Home to review reported changes and History for reversible changes. Owner Mode can restore supported choices when you explicitly enable it.",
            "Owner Mode covers a supported catalog, not every setting or app. Review its status and scope in Settings before enabling it."));
        lessons.Add(new("Your pack", "Review your desktop pack",
            "The Desktop Grand Pack combines cleanup recommendations. Your module choices decide which recommendations start selected.",
            "A single preset cannot decide which apps you need. Review every app removal and keep anything you use.",
            "Review pack opens Presets. Check the live values, deselect individual entries, then stage your choices for final review.",
            "Removals affect all users and can delete app data. They have no undo. Save what you need before applying; reinstall separately."));
        Lessons = lessons;
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
                MachineFormFactor.Desktop => "Desktop verified. Changes wait for your review.",
                MachineFormFactor.Laptop => "This guide and pack support desktops only. All other app modules remain available.",
                _ => "Desktop type could not be verified. You can read the guide, but the pack is unavailable.",
            };
        }
        catch (Exception ex)
        {
            NLog.LogManager.GetCurrentClassLogger().Warn(ex, "Setup desktop check failed");
            IsDesktop = false;
            DeviceStatus = "Desktop check failed. Reopen the guide to retry. Other modules remain available.";
        }
    }

    partial void OnSelectedIndexChanged(int value)
    {
        foreach (var property in new[] { nameof(Current), nameof(Progress), nameof(HasPrevious), nameof(HasNext), nameof(IsLast), nameof(CanOpenModule) })
            OnPropertyChanged(property);
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
public sealed partial class SetupLessonViewModel(string label, string title, string controls, string behavior, string choice, string upkeep) : ViewModelBase
{
    public string Label { get; } = label;
    public string Title { get; } = title;
    public string Controls { get; } = controls;
    public string Behavior { get; } = behavior;
    public string Choice { get; } = choice;
    public string Upkeep { get; } = upkeep;
    public string? ModuleName { get; set; }
    public bool IsAvailable { get; set; }
    public string Availability { get; set; } = "Read each lesson, or choose a module from the list.";
    public bool HasRecommendations => ModuleName is "Windows Annoyances" or "Privacy & Telemetry" or "Explorer" or "Software";
    [ObservableProperty] private bool _includeRecommendations = true;
}
