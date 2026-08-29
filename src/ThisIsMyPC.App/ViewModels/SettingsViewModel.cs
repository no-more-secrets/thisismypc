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
        _selected = options.FirstOrDefault(o => o.Value == initialValue)
            ?? (options.Count > 0 ? options[0] : null);
        _applied = applied;
    }

    partial void OnSelectedChanged(SettingChoiceOption? value)
    {
        if (value is null)
            return;
        if (Options.Count == 0)
            return;
        if (_moduleId is null)
            _settings.SetApp(_key, value.Value);
        else
            _settings.SetModule(_moduleId, _key, value.Value);
        _applied?.Invoke(value.Value);
    }
}

/// <summary>A free-text row (TextBox); persists on every keystroke like the others.</summary>
public sealed partial class SettingTextItemViewModel : ViewModelBase
{
    private readonly ISettingsService _settings;
    private readonly string? _moduleId;
    private readonly string _key;

    [ObservableProperty]
    private string _text;

    public string DisplayName { get; }
    public string Description { get; }

    public SettingTextItemViewModel(
        ISettingsService settings, string? moduleId, string key,
        string displayName, string description, string initial)
    {
        _settings = settings;
        _moduleId = moduleId;
        _key = key;
        DisplayName = displayName;
        Description = description;
        _text = initial;
    }

    partial void OnTextChanged(string value)
    {
        if (_moduleId is null)
            _settings.SetApp(_key, value);
        else
            _settings.SetModule(_moduleId, _key, value);
    }
}

/// <summary>Presentation wrapper for one import-preview row.</summary>
public sealed class SettingsImportRowWrapper
{
    public SettingsImportRowWrapper(SettingsImportRow row)
    {
        var scope = row.Scope == SettingChangedEventArgs.AppScope ? "App" : row.Scope;
        Display = row.SkipReason is null
            ? $"{scope} / {row.Key}: {row.CurrentValue ?? "(unset)"} -> {row.ImportedValue}"
            : $"{scope} / {row.Key}: skipped - {row.SkipReason}";
    }

    public string Display { get; }
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
/// every change. Export/import (7-4) lives here too — file dialogs are handled by the
/// view's code-behind, the VM works on JSON strings so it stays testable.
/// </summary>
public sealed partial class SettingsViewModel : ViewModelBase
{
    private readonly ISettingsService _settings;
    private readonly IReadOnlyCollection<string> _installedModuleIds;
    private readonly string _appVersion;
    private SettingsImportPreview? _pendingImport;

    public ObservableCollection<SettingsSectionViewModel> Sections { get; } = [];

    public SettingsViewModel(
        ISettingsService settings,
        IReadOnlyList<IModuleSettingsContributor> moduleContributors,
        Action<string>? applyTheme = null,
        IReadOnlyCollection<string>? installedModuleIds = null,
        string? appVersion = null,
        IReadOnlyList<Core.Services.CapabilityReportRow>? capabilityReport = null)
    {
        SystemCapabilityRows = (capabilityReport ?? [])
            .Select(r => new FirstLaunchRowViewModel(
                r.DisplayName,
                r.Availability.IsAvailable
                    ? $"Available. {r.Availability.RemediationHint}".Trim()
                    : $"{r.Availability.Reason} {r.Availability.RemediationHint}".Trim(),
                r.Availability.IsAvailable))
            .ToList();
        _settings = settings;
        _installedModuleIds = installedModuleIds ?? [];
        _appVersion = appVersion ?? "0.0.0";
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
                    "Tray options need Tray mode enabled below; without it the app falls back to exiting.",
                    [new("exit", "Exit the app"), new("tray", "Keep running in the tray"), new("taskbar", "Minimize to taskbar")],
                    settings.GetApp(AppSettingKeys.CloseAction, "exit")),
                new SettingChoiceItemViewModel(
                    settings, null, AppSettingKeys.MinimizeAction,
                    "When I minimize the window",
                    "Tray option needs Tray mode enabled below; without it the app minimizes to the taskbar.",
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
                    "Master switch for all notifications. Off means events are only visible inside the app.",
                    settings.GetAppBool(AppSettingKeys.Notifications, true)),
                new SettingToggleItemViewModel(
                    settings, null, AppSettingKeys.NotifyMonitoring,
                    "Notify: monitoring alerts",
                    "New startup entries or services detected by background monitoring.",
                    settings.GetAppBool(AppSettingKeys.NotifyMonitoring, true)),
                new SettingToggleItemViewModel(
                    settings, null, AppSettingKeys.NotifyUpdates,
                    "Notify: update available",
                    "A newer ThisIsMyPC release was found by the launch check.",
                    settings.GetAppBool(AppSettingKeys.NotifyUpdates, true)),
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

    /// <summary>Read-only capability summary (5-2 AC3) — the first-launch info, always reachable.</summary>
    public IReadOnlyList<FirstLaunchRowViewModel> SystemCapabilityRows { get; }

    public bool HasCapabilityRows => SystemCapabilityRows.Count > 0;

    // --- 7-4 export/import ---

    [ObservableProperty]
    private string _transferStatus = string.Empty;

    [ObservableProperty]
    private bool _hasImportPreview;

    [ObservableProperty]
    private string _importPreviewSummary = string.Empty;

    public ObservableCollection<SettingsImportRowWrapper> ImportPreviewRows { get; } = [];

    public string BuildExportJson() =>
        SettingsTransfer.BuildExportJson(_settings, _appVersion, Environment.MachineName);

    public string DefaultExportFileName =>
        SettingsTransfer.DefaultExportFileName(DateTimeOffset.Now);

    public void ReportExport(string filePath) =>
        TransferStatus = $"Settings exported to {filePath}";

    /// <summary>False when the file is not a valid export.</summary>
    public bool LoadImportPreview(string json)
    {
        var document = SettingsTransfer.Parse(json);
        if (document is null)
        {
            TransferStatus = "That file is not a ThisIsMyPC settings export.";
            return false;
        }

        _pendingImport = SettingsTransfer.BuildPreview(_settings, document, _installedModuleIds);
        ImportPreviewRows.Clear();
        foreach (var row in _pendingImport.Rows)
            ImportPreviewRows.Add(new SettingsImportRowWrapper(row));

        var source = _pendingImport.SourceMachineName is { Length: > 0 } machine
            ? $" from {machine}" : string.Empty;
        ImportPreviewSummary =
            $"Importing {_pendingImport.ApplicableCount} setting(s){source}; {_pendingImport.SkippedCount} will be skipped.";
        HasImportPreview = true;
        TransferStatus = string.Empty;
        return true;
    }

    [CommunityToolkit.Mvvm.Input.RelayCommand]
    private void ApplyImport()
    {
        if (_pendingImport is null)
            return;

        var (applied, skipped) = SettingsTransfer.Apply(_settings, _pendingImport);
        TransferStatus = $"Settings imported successfully - {applied} applied, {skipped} skipped. Reopen Settings to see the new values.";
        ClearImportPreview();
    }

    [CommunityToolkit.Mvvm.Input.RelayCommand]
    private void CancelImport()
    {
        TransferStatus = "Import cancelled.";
        ClearImportPreview();
    }

    private void ClearImportPreview()
    {
        _pendingImport = null;
        ImportPreviewRows.Clear();
        HasImportPreview = false;
        ImportPreviewSummary = string.Empty;
    }

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
            _ => new SettingTextItemViewModel(
                settings, moduleId, definition.Key, definition.DisplayName,
                definition.Description, current),
        };
    }
}
