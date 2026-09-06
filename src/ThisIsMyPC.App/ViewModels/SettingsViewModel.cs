using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using ThisIsMyPC.Core.Settings;

namespace ThisIsMyPC.App.ViewModels;

/// <summary>One dark/light-style option in a choice setting.</summary>
public sealed record SettingChoiceOption(string Value, string DisplayName);

/// <summary>
/// A toggle row. Writes through ISettingsService immediately; app preferences are not
/// system mutations and never touch the pending-changes pipeline.
/// </summary>
public sealed partial class SettingToggleItemViewModel : ViewModelBase
{
    private readonly ISettingsService _settings;
    private readonly string? _moduleId; // null = app scope
    private readonly string _key;

    [ObservableProperty]
    private bool _isOn;

    [ObservableProperty]
    private bool _isEnabled = true;

    private readonly Action<bool>? _applied;

    public string DisplayName { get; }
    public string Description { get; }

    public SettingToggleItemViewModel(
        ISettingsService settings, string? moduleId, string key,
        string displayName, string description, bool initial, Action<bool>? applied = null)
    {
        _settings = settings;
        _moduleId = moduleId;
        _key = key;
        DisplayName = displayName;
        Description = description;
        _isOn = initial;
        _applied = applied;
    }

    partial void OnIsOnChanged(bool value)
    {
        _applied?.Invoke(value);
        if (_moduleId is null)
            _settings.SetApp(_key, value ? "1" : "0");
        else
            _settings.SetModule(_moduleId, _key, value ? "1" : "0");
        if (_moduleId is null && _key == AppSettingKeys.TrayMode)
        {
            _settings.SetApp(AppSettingKeys.CloseAction, value ? "tray" : "exit");
            _settings.SetApp(AppSettingKeys.MinimizeAction, "taskbar");
        }
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

    /// <summary>False when the tab already carries the section's name, so the page does not say it twice.</summary>
    public bool ShowHeader { get; init; } = true;

    public string? Subtitle { get; init; }
    public required IReadOnlyList<object> Items { get; init; }
}

/// <summary>
/// The application settings screen, one tab per concern: Application (look,
/// tray, startup, update check), Notifications, Owner Mode (the service plus
/// the app's own monitoring, which does not need the service), Modules
/// (module preferences and the read-only capability summary), and Backup &amp;
/// Transfer. Values live in ISettingsService and persist on every change.
/// Export/import lives here too; file dialogs are handled by the view's
/// code-behind, the VM works on JSON strings so it stays testable.
/// </summary>
public sealed partial class SettingsViewModel : ViewModelBase, ITabbedPage
{
    public const string ApplicationHeader = "Application";
    public const string NotificationsHeader = "Notifications";
    public const string MonitoringHeader = "In-app monitoring";

    [ObservableProperty]
    private int _selectedTabIndex;

    private readonly ISettingsService _settings;
    private readonly IReadOnlyCollection<string> _installedModuleIds;
    private readonly string _appVersion;
    private SettingsImportPreview? _pendingImport;

    /// <summary>Every section in tab order: the three app sections, then one per module contributor.</summary>
    public ObservableCollection<SettingsSectionViewModel> Sections { get; } = [];
    public SettingsSectionViewModel ApplicationSection { get; }
    public SettingsSectionViewModel NotificationsSection { get; }
    public SettingsSectionViewModel MonitoringSection { get; }
    public IReadOnlyList<SettingsSectionViewModel> ApplicationSections => [ApplicationSection];
    public IReadOnlyList<SettingsSectionViewModel> NotificationSections => [NotificationsSection];
    public IReadOnlyList<SettingsSectionViewModel> MonitoringSections => [MonitoringSection];
    public IReadOnlyList<SettingsSectionViewModel> ModuleSections { get; }

    /// <summary>Owner Mode service lifecycle section; null when unavailable (tests).</summary>
    public OwnerModeSectionViewModel? OwnerMode { get; }

    public bool HasOwnerModeSection => OwnerMode is not null;

    public SettingsViewModel(
        ISettingsService settings,
        IReadOnlyList<IModuleSettingsContributor> moduleContributors,
        Action<string>? applyTheme = null,
        IReadOnlyCollection<string>? installedModuleIds = null,
        string? appVersion = null,
        IReadOnlyList<Core.Services.CapabilityReportRow>? capabilityReport = null,
        OwnerModeSectionViewModel? ownerMode = null)
    {
        OwnerMode = ownerMode;
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
        var automaticDownloads = new SettingToggleItemViewModel(
            settings, null, AppSettingKeys.AutoDownloadUpdates,
            "Automatically download updates",
            "Downloads updates when available. Install them with Restart ThisIsMyPC.",
            settings.GetAppBool(AppSettingKeys.AutoDownloadUpdates, false))
        {
            IsEnabled = settings.GetAppBool(AppSettingKeys.UpdateCheck, true),
        };
        ApplicationSection = new SettingsSectionViewModel
        {
            Header = ApplicationHeader,
            ShowHeader = false,
            Items =
            [
                new SettingChoiceItemViewModel(
                    settings, null, AppSettingKeys.Theme,
                    "Theme",
                    "",
                    [new("dark", "Dark"), new("light", "Light"), new("system", "System")],
                    settings.GetApp(AppSettingKeys.Theme, "dark"),
                    applyTheme),
                new SettingToggleItemViewModel(
                    settings, null, AppSettingKeys.DyslexiaFont,
                    "Dyslexia-friendly font",
                    "Switches body text to OpenDyslexic.",
                    settings.GetAppBool(AppSettingKeys.DyslexiaFont, false)),
                new SettingToggleItemViewModel(
                    settings, null, AppSettingKeys.TrayMode,
                    "Tray mode",
                    "Keep the app running in the tray when you close its window.",
                    settings.GetAppBool(AppSettingKeys.TrayMode, false)),
                new SettingChoiceItemViewModel(
                    settings, null, AppSettingKeys.AutoStart,
                    "Start with Windows", "",
                    [new("0", "Disabled"), new("1", "Minimized"), new("2", "Open at logon")],
                    settings.GetApp(AppSettingKeys.AutoStart, "0")),
                new SettingToggleItemViewModel(
                    settings, null, AppSettingKeys.UpdateCheck,
                    "Check for app updates",
                    "Compares the app version against GitHub Releases at launch. Only version numbers are sent; turn off for offline use.",
                    settings.GetAppBool(AppSettingKeys.UpdateCheck, true),
                    enabled => automaticDownloads.IsEnabled = enabled),
                automaticDownloads,
            ],
        };

        NotificationsSection = new SettingsSectionViewModel
        {
            Header = NotificationsHeader,
            ShowHeader = false,
            Subtitle = "Windows notifications from the app. Off keeps events in-app only.",
            Items =
            [
                new SettingToggleItemViewModel(
                    settings, null, AppSettingKeys.Notifications,
                    "Notifications",
                    "Master switch for every notification below.",
                    settings.GetAppBool(AppSettingKeys.Notifications, true)),
                new SettingToggleItemViewModel(
                    settings, null, AppSettingKeys.NotifyMonitoring,
                    "Notify: monitoring alerts",
                    "New startup entries or services were detected.",
                    settings.GetAppBool(AppSettingKeys.NotifyMonitoring, true)),
                new SettingToggleItemViewModel(
                    settings, null, AppSettingKeys.NotifyUpdates,
                    "Notify: update available",
                    "A newer release was found at launch.",
                    settings.GetAppBool(AppSettingKeys.NotifyUpdates, true)),
            ],
        };

        // The app's own watcher, not the service: it runs inside the app while
        // the window or tray icon is up and needs nothing installed.
        MonitoringSection = new SettingsSectionViewModel
        {
            Header = MonitoringHeader,
            Subtitle = "Runs inside the app while it is open. Does not need the Owner Mode service.",
            Items =
            [
                new SettingToggleItemViewModel(
                    settings, null, AppSettingKeys.MonitoringEnabled,
                    "Startup & service monitoring",
                    "Watches for new startup entries, services, and scheduled tasks while the app runs. Detections appear on Home.",
                    settings.GetAppBool(AppSettingKeys.MonitoringEnabled, false)),
            ],
        };

        Sections.Add(ApplicationSection);
        Sections.Add(NotificationsSection);
        Sections.Add(MonitoringSection);

        var moduleSections = new List<SettingsSectionViewModel>();
        foreach (var contributor in moduleContributors.OrderBy(c => c.ModuleId, StringComparer.Ordinal))
        {
            if (contributor.SettingDefinitions.Count == 0)
                continue;

            var section = new SettingsSectionViewModel
            {
                Header = contributor.ModuleId,
                Items = contributor.SettingDefinitions.Select(d => BuildModuleItem(settings, contributor.ModuleId, d)).ToList(),
            };
            moduleSections.Add(section);
            Sections.Add(section);
        }

        ModuleSections = moduleSections;
        HasModuleSections = moduleSections.Count > 0;
    }

    /// <summary>True when any installed module contributes preferences; the Modules tab stays for the capability summary either way.</summary>
    public bool HasModuleSections { get; }

    /// <summary>Read-only capability summary; the first-launch info, always reachable.</summary>
    public IReadOnlyList<FirstLaunchRowViewModel> SystemCapabilityRows { get; }

    public bool HasCapabilityRows => SystemCapabilityRows.Count > 0;

    // --- export/import ---

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
