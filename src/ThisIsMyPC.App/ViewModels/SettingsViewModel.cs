using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using ThisIsMyPC.Core.Settings;

namespace ThisIsMyPC.App.ViewModels;

/// <summary>One dark/light-style option in a choice setting.</summary>
public sealed record SettingChoiceOption(string Value, string DisplayName);

/// <summary>
/// A toggle row. Writes through ISettingsService immediately — app preferences are not
/// system mutations and never touch the pending-changes pipeline.
/// </summary>
public sealed partial class SettingToggleItemViewModel : ViewModelBase
{
    private readonly ISettingsService _settings;
    private readonly string? _moduleId; // null = app scope
    private readonly string _key;

    [ObservableProperty]
    private bool _isOn;

    public string DisplayName { get; }
    public string Description { get; }

    public SettingToggleItemViewModel(
        ISettingsService settings, string? moduleId, string key,
        string displayName, string description, bool initial)
    {
        _settings = settings;
        _moduleId = moduleId;
        _key = key;
        DisplayName = displayName;
        Description = description;
        _isOn = initial;
    }

    partial void OnIsOnChanged(bool value)
    {
        if (_moduleId is null)
            _settings.SetApp(_key, value ? "1" : "0");
        else
            _settings.SetModule(_moduleId, _key, value ? "1" : "0");
    }
}

/// <summary>A choice row (ComboBox).</summary>
public sealed partial class SettingChoiceItemViewModel : ViewModelBase
{
    private readonly ISettingsService _settings;
    private readonly string? _moduleId;
    private readonly string _key;
    private readonly Action<string>? _applied;

    [ObservableProperty]
    private SettingChoiceOption? _selected;

    public string DisplayName { get; }
    public string Description { get; }
    public IReadOnlyList<SettingChoiceOption> Options { get; }

    public SettingChoiceItemViewModel(
        ISettingsService settings, string? moduleId, string key,
        string displayName, string description,
        IReadOnlyList<SettingChoiceOption> options, string initialValue,
        Action<string>? applied = null)
    {
        _settings = settings;
        _moduleId = moduleId;
        _key = key;
        DisplayName = displayName;
        Description = description;
        Options = options;
        _selected = options.FirstOrDefault(o => o.Value == initialValue) ?? options[0];
        _applied = applied;
    }

    partial void OnSelectedChanged(SettingChoiceOption? value)
    {
        if (value is null)
            return;
        if (_moduleId is null)
            _settings.SetApp(_key, value.Value);
        else
            _settings.SetModule(_moduleId, _key, value.Value);
        _applied?.Invoke(value.Value);
    }
}

public sealed class SettingsSectionViewModel
{
    public required string Header { get; init; }
    public string? Subtitle { get; init; }
    public required IReadOnlyList<object> Items { get; init; }
}

/// <summary>
/// The application settings screen (7-2): General + Persistence app preferences plus
/// module-contributed settings (FR6). Values live in ISettingsService and persist on
/// every change.
/// </summary>
public sealed class SettingsViewModel : ViewModelBase
{
    public ObservableCollection<SettingsSectionViewModel> Sections { get; } = [];

    public SettingsViewModel(
        ISettingsService settings,
        IReadOnlyList<IModuleSettingsContributor> moduleContributors,
        Action<string>? applyTheme = null)
    {
        Sections.Add(new SettingsSectionViewModel
        {
            Header = "General",
            Items =
            [
                new SettingChoiceItemViewModel(
                    settings, null, AppSettingKeys.Theme,
                    "Theme",
                    "Dark is the only finished palette today - the light palette arrives with the UI/UX overhaul. Your choice is remembered.",
                    [new("dark", "Dark"), new("light", "Light (coming with UI overhaul)")],
                    settings.GetApp(AppSettingKeys.Theme, "dark"),
                    applyTheme),
                new SettingChoiceItemViewModel(
                    settings, null, AppSettingKeys.CloseAction,
                    "When I close the window",
                    "Takes effect when background features arrive (Epic 9).",
                    [new("exit", "Exit the app"), new("tray", "Keep running in the tray")],
                    settings.GetApp(AppSettingKeys.CloseAction, "exit")),
                new SettingChoiceItemViewModel(
                    settings, null, AppSettingKeys.MinimizeAction,
                    "When I minimize the window",
                    "Takes effect when background features arrive (Epic 9).",
                    [new("taskbar", "Minimize to taskbar"), new("tray", "Minimize to tray")],
                    settings.GetApp(AppSettingKeys.MinimizeAction, "taskbar")),
                new SettingToggleItemViewModel(
                    settings, null, AppSettingKeys.DyslexiaFont,
                    "Dyslexia-friendly font",
                    "Renders the app in an accessibility-focused font. Wires up with story 10-4.",
                    settings.GetAppBool(AppSettingKeys.DyslexiaFont, false)),
            ],
        });

        Sections.Add(new SettingsSectionViewModel
        {
            Header = "Persistence & Background",
            Subtitle = "These configure the opt-in background features; the behavior itself lands with Epic 9.",
            Items =
            [
                new SettingToggleItemViewModel(
                    settings, null, AppSettingKeys.TrayMode,
                    "Tray mode",
                    "Show a tray icon and allow the app to keep running in the background.",
                    settings.GetAppBool(AppSettingKeys.TrayMode, false)),
                new SettingToggleItemViewModel(
                    settings, null, AppSettingKeys.AutoStart,
                    "Start with Windows",
                    "Launch ThisIsMyPC automatically at sign-in.",
                    settings.GetAppBool(AppSettingKeys.AutoStart, false)),
                new SettingToggleItemViewModel(
                    settings, null, AppSettingKeys.Notifications,
                    "Notifications",
                    "Allow monitoring notifications (new startup entries, reverted settings).",
                    settings.GetAppBool(AppSettingKeys.Notifications, true)),
                new SettingToggleItemViewModel(
                    settings, null, AppSettingKeys.UpdateCheck,
                    "Check for updates",
                    "On launch, compare the app version against GitHub Releases. Only version numbers are transmitted - never telemetry or system data. Turn off for fully offline use.",
                    settings.GetAppBool(AppSettingKeys.UpdateCheck, true)),
            ],
        });

        foreach (var contributor in moduleContributors.OrderBy(c => c.ModuleId, StringComparer.Ordinal))
        {
            if (contributor.SettingDefinitions.Count == 0)
                continue;

            Sections.Add(new SettingsSectionViewModel
            {
                Header = contributor.ModuleId,
                Items = contributor.SettingDefinitions.Select(d => BuildModuleItem(settings, contributor.ModuleId, d)).ToList(),
            });
        }

        HasModuleSections = Sections.Count > 2;
    }

    public bool HasModuleSections { get; }

    public string ModulePreferencesPlaceholder =>
        "No installed module exposes configurable settings yet. Module preferences appear here automatically when they do.";

    private static object BuildModuleItem(
        ISettingsService settings, string moduleId, ModuleSettingDefinition definition)
    {
        var current = settings.GetModule(moduleId, definition.Key) ?? definition.DefaultValue;

        return definition.Type switch
        {
            ModuleSettingType.Toggle => new SettingToggleItemViewModel(
                settings, moduleId, definition.Key, definition.DisplayName,
                definition.Description, current == "1"),
            ModuleSettingType.Choice => new SettingChoiceItemViewModel(
                settings, moduleId, definition.Key, definition.DisplayName,
                definition.Description,
                definition.Options?.Select(o => new SettingChoiceOption(o.Value, o.DisplayName)).ToList()
                    ?? [new SettingChoiceOption(current, current)],
                current),
            _ => new SettingChoiceItemViewModel(
                settings, moduleId, definition.Key, definition.DisplayName,
                definition.Description,
                [new SettingChoiceOption(current, current)],
                current),
        };
    }
}
