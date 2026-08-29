using ThisIsMyPC.Core.Services;
using ThisIsMyPC.Core.Settings;

namespace ThisIsMyPC.App.Services;

/// <summary>
/// Owns the "Start with Windows" HKCU Run entry (9-2). No entry exists by default;
/// the setting toggle creates/removes it live, and Reconcile() at startup repairs any
/// drift between the setting and the registry (e.g. an external cleaner removed the
/// key while the setting stayed on).
/// </summary>
public sealed class AutoStartService : IDisposable
{
    public const string RunKeyPath = @"HKCU\Software\Microsoft\Windows\CurrentVersion\Run";
    public const string RunValueName = "ThisIsMyPC";

    private readonly IRegistryService _registry;
    private readonly ISettingsService _settings;
    private readonly string _launchCommand;

    public AutoStartService(IRegistryService registry, ISettingsService settings, string? exePath = null)
    {
        ArgumentNullException.ThrowIfNull(registry);
        ArgumentNullException.ThrowIfNull(settings);
        _registry = registry;
        _settings = settings;

        var path = exePath ?? Environment.ProcessPath ?? string.Empty;
        _launchCommand = $"\"{path}\" --minimized";

        _settings.SettingChanged += OnSettingChanged;
    }

    /// <summary>Startup repair: make the registry match the setting.</summary>
    public void Reconcile() => Apply(_settings.GetAppBool(AppSettingKeys.AutoStart, fallback: false));

    private void OnSettingChanged(object? sender, SettingChangedEventArgs e)
    {
        if (e is { Scope: SettingChangedEventArgs.AppScope, Key: AppSettingKeys.AutoStart })
            Apply(e.Value == "1");
    }

    private void Apply(bool enabled)
    {
        if (enabled)
        {
            if (_launchCommand.StartsWith("\"\"", StringComparison.Ordinal))
            {
                Serilog.Log.Warning("Auto-start: executable path unknown; not writing a Run entry");
                return;
            }
            var result = _registry.WriteString(RunKeyPath, RunValueName, _launchCommand);
            if (!result.IsSuccess)
                Serilog.Log.Warning("Auto-start entry write failed: {Error}", result.ErrorMessage);
        }
        else
        {
            // Only delete when present — never churn the Run key on every clean start.
            if (_registry.ValueExists(RunKeyPath, RunValueName) is { IsSuccess: true, Value: true })
            {
                var result = _registry.DeleteValue(RunKeyPath, RunValueName);
                if (!result.IsSuccess)
                    Serilog.Log.Warning("Auto-start entry removal failed: {Error}", result.ErrorMessage);
            }
        }
    }

    public void Dispose() => _settings.SettingChanged -= OnSettingChanged;
}
