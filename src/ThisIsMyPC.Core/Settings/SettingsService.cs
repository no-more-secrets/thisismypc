using System.Text.Json;

namespace ThisIsMyPC.Core.Settings;

public sealed class SettingsService : ISettingsService
{
    private readonly string _filePath;
    private readonly Lock _sync = new();

    private Dictionary<string, string> _appSettings = new(StringComparer.Ordinal);
    private Dictionary<string, Dictionary<string, string>> _moduleSettings = new(StringComparer.Ordinal);
    private Dictionary<string, JsonElement>? _extensionData;
    private bool _initialized;

    // An UNREADABLE file (IO/access) must not let a later Set rewrite the file from
    // defaults and destroy the user's real settings — saves are disabled for the
    // session (DisplayModePreferencesStore semantics). A CORRUPT file is different:
    // it is preserved as .bad, then replaced with defaults.
    private bool _saveDisabled;

    public SettingsService(string? filePath = null)
    {
        _filePath = filePath ?? Path.Combine(AppConstants.DataDirectoryPath, "settings.json");
    }

    public bool SettingsWereReset { get; private set; }

    public string? LoadError { get; private set; }

    public event EventHandler<SettingChangedEventArgs>? SettingChanged;

    public void Initialize()
    {
        lock (_sync)
        {
            if (_initialized)
                return;
            _initialized = true;

            if (!File.Exists(_filePath))
            {
                ApplyDefaults();
                Save();
                return;
            }

            string json;
            try
            {
                json = File.ReadAllText(_filePath);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                LoadError = $"Settings file unreadable ({ex.GetType().Name}): {ex.Message}";
                ApplyDefaults();
                _saveDisabled = true;
                return;
            }

            SettingsDocument? document = null;
            try
            {
                document = JsonSerializer.Deserialize(json, SettingsJsonContext.Default.SettingsDocument);
            }
            catch (JsonException ex)
            {
                LoadError = $"Settings file corrupt: {ex.Message}";
            }

            if (document is null)
            {
                // Corrupt: keep the evidence, reset to defaults, rewrite.
                SettingsWereReset = true;
                LoadError ??= "Settings file corrupt: deserialized to null";
                PreserveCorruptFile();
                ApplyDefaults();
                Save();
                return;
            }

            ApplyDefaults();
            try
            {
                // Null-tolerant: JSON like {"theme": null} or {"Module": null} parses
                // fine but violates the non-null value contract — skip those entries
                // rather than crash at startup.
                foreach (var (key, value) in document.AppSettings ?? [])
                {
                    if (value is not null)
                        _appSettings[key] = value;
                }
                foreach (var (moduleId, values) in document.ModuleSettings ?? [])
                {
                    if (values is null)
                        continue;
                    var copy = new Dictionary<string, string>(StringComparer.Ordinal);
                    foreach (var (key, value) in values)
                    {
                        if (value is not null)
                            copy[key] = value;
                    }
                    _moduleSettings[moduleId] = copy;
                }
                _extensionData = document.ExtensionData;
            }
#pragma warning disable CA1031 // any structurally-hostile file routes to the corrupt path, never a startup crash
            catch (Exception ex)
            {
                SettingsWereReset = true;
                LoadError = $"Settings file corrupt: {ex.Message}";
                PreserveCorruptFile();
                ApplyDefaults();
                Save();
            }
#pragma warning restore CA1031
        }
    }

    public string GetApp(string key, string fallback)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);
        lock (_sync)
        {
            EnsureInitialized();
            return _appSettings.TryGetValue(key, out var value) ? value : fallback;
        }
    }

    public bool GetAppBool(string key, bool fallback)
        => GetApp(key, fallback ? "1" : "0") == "1";

    public void SetApp(string key, string value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);
        ArgumentNullException.ThrowIfNull(value);
        lock (_sync)
        {
            EnsureInitialized();
            _appSettings[key] = value;
            Save();
        }

        SettingChanged?.Invoke(this, new SettingChangedEventArgs
        {
            Scope = SettingChangedEventArgs.AppScope,
            Key = key,
            Value = value,
        });
    }

    public string? GetModule(string moduleId, string key)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(moduleId);
        ArgumentException.ThrowIfNullOrWhiteSpace(key);
        lock (_sync)
        {
            EnsureInitialized();
            return _moduleSettings.TryGetValue(moduleId, out var values)
                && values.TryGetValue(key, out var value) ? value : null;
        }
    }

    public void SetModule(string moduleId, string key, string value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(moduleId);
        ArgumentException.ThrowIfNullOrWhiteSpace(key);
        ArgumentNullException.ThrowIfNull(value);
        lock (_sync)
        {
            EnsureInitialized();
            if (!_moduleSettings.TryGetValue(moduleId, out var values))
                _moduleSettings[moduleId] = values = new Dictionary<string, string>(StringComparer.Ordinal);
            values[key] = value;
            Save();
        }

        SettingChanged?.Invoke(this, new SettingChangedEventArgs
        {
            Scope = moduleId,
            Key = key,
            Value = value,
        });
    }

    public IReadOnlyDictionary<string, string> SnapshotApp()
    {
        lock (_sync)
        {
            EnsureInitialized();
            return new Dictionary<string, string>(_appSettings, StringComparer.Ordinal);
        }
    }

    public IReadOnlyDictionary<string, IReadOnlyDictionary<string, string>> SnapshotModules()
    {
        lock (_sync)
        {
            EnsureInitialized();
            return _moduleSettings.ToDictionary(
                kv => kv.Key,
                kv => (IReadOnlyDictionary<string, string>)new Dictionary<string, string>(kv.Value, StringComparer.Ordinal),
                StringComparer.Ordinal);
        }
    }

    private void EnsureInitialized()
    {
        if (!_initialized)
            Initialize();
    }

    private void ApplyDefaults()
    {
        _appSettings = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var (key, value) in AppSettingKeys.Defaults)
            _appSettings[key] = value;
        _moduleSettings = new Dictionary<string, Dictionary<string, string>>(StringComparer.Ordinal);
    }

    private void PreserveCorruptFile()
    {
        try
        {
            File.Copy(_filePath, _filePath + ".bad", overwrite: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Best-effort diagnostics only.
        }
    }

    private void Save()
    {
        if (_saveDisabled)
            return;

        try
        {
            var directory = Path.GetDirectoryName(_filePath);
            if (!string.IsNullOrEmpty(directory))
                Directory.CreateDirectory(directory);

            var document = new SettingsDocument
            {
                AppSettings = _appSettings,
                ModuleSettings = _moduleSettings,
                ExtensionData = _extensionData,
            };
            // Atomic swap: a power loss mid-write must never leave a truncated live file.
            var tempPath = _filePath + ".tmp";
            File.WriteAllText(
                tempPath,
                JsonSerializer.Serialize(document, SettingsJsonContext.Default.SettingsDocument));
            File.Move(tempPath, _filePath, overwrite: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Persistence is best-effort; the in-memory value still applies.
        }
    }
}
