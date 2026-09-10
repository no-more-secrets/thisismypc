using ThisIsMyPC.Core.Settings;

namespace ThisIsMyPC.Modules.Hardware.Tests.Fakes;

/// <summary>In-memory ISettingsService: app scope and module scope, defaults from AppSettingKeys.</summary>
public sealed class FakeSettingsService : ISettingsService
{
    private readonly Dictionary<string, string> _app = new(StringComparer.Ordinal);
    private readonly Dictionary<string, Dictionary<string, string>> _modules = new(StringComparer.Ordinal);

    public event EventHandler<SettingChangedEventArgs>? SettingChanged;

    public string? LoadError => null;
    public bool SettingsWereReset => false;

    public void Initialize()
    {
    }

    public string GetApp(string key, string fallback) =>
        _app.TryGetValue(key, out var value) ? value : AppSettingKeys.Defaults.GetValueOrDefault(key, fallback);

    public bool GetAppBool(string key, bool fallback) => GetApp(key, fallback ? "1" : "0") == "1";

    public void SetApp(string key, string value)
    {
        _app[key] = value;
        SettingChanged?.Invoke(this, new SettingChangedEventArgs { Scope = SettingChangedEventArgs.AppScope, Key = key, Value = value });
    }

    public string? GetModule(string moduleId, string key) =>
        _modules.TryGetValue(moduleId, out var values) && values.TryGetValue(key, out var value) ? value : null;

    public void SetModule(string moduleId, string key, string value)
    {
        if (!_modules.TryGetValue(moduleId, out var values))
            _modules[moduleId] = values = new Dictionary<string, string>(StringComparer.Ordinal);
        values[key] = value;
        SettingChanged?.Invoke(this, new SettingChangedEventArgs { Scope = moduleId, Key = key, Value = value });
    }

    public IReadOnlyDictionary<string, string> SnapshotApp() => _app;

    public IReadOnlyDictionary<string, IReadOnlyDictionary<string, string>> SnapshotModules() =>
        _modules.ToDictionary(p => p.Key, p => (IReadOnlyDictionary<string, string>)p.Value, StringComparer.Ordinal);
}
