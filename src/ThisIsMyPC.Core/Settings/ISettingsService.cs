namespace ThisIsMyPC.Core.Settings;

public sealed class SettingChangedEventArgs : EventArgs
{
    public required string Scope { get; init; }
    public required string Key { get; init; }
    public required string Value { get; init; }

    /// <summary>Scope value for application-level settings; module settings use the module id.</summary>
    public const string AppScope = "app";
}

/// <summary>
/// Persistent key-value settings (FR74): application-level plus per-module scopes,
/// stored as human-readable JSON at %ProgramData%\ThisIsMyPC\settings.json. Every Set
/// persists immediately; there is no manual save.
/// </summary>
public interface ISettingsService
{
    /// <summary>Loads the settings file (creating defaults when missing). Call once at startup.</summary>
    void Initialize();

    /// <summary>True when a corrupt settings file was replaced with defaults this session.</summary>
    bool SettingsWereReset { get; }

    /// <summary>Diagnostic detail for a reset/unreadable settings file (Core stays log-free; the App logs this).</summary>
    string? LoadError { get; }

    string GetApp(string key, string fallback);
    bool GetAppBool(string key, bool fallback);
    void SetApp(string key, string value);

    string? GetModule(string moduleId, string key);
    void SetModule(string moduleId, string key, string value);

    /// <summary>Copy of all app-scope values (7-4 export).</summary>
    IReadOnlyDictionary<string, string> SnapshotApp();

    /// <summary>Copy of all module-scope values keyed by module id (7-4 export).</summary>
    IReadOnlyDictionary<string, IReadOnlyDictionary<string, string>> SnapshotModules();

    event EventHandler<SettingChangedEventArgs>? SettingChanged;
}
