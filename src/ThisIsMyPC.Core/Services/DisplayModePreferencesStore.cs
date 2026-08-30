namespace ThisIsMyPC.Core.Services;

/// <summary>
/// Persists per-tab display-mode preferences (Epic 10.2): one line per tab,
/// "tabKey=registryData|compact" with 1/0 values. Corrupt lines are ignored; a
/// missing file means all defaults. Global defaults arrive with Epic 7's
/// preferences UI.
/// </summary>
public sealed class DisplayModePreferencesStore
{
    private readonly string _filePath;
    private readonly Lock _sync = new();
    private Dictionary<string, (bool RegistryData, bool Compact)>? _modes;
    private bool _loadFailed;

    public DisplayModePreferencesStore(string filePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(filePath);
        _filePath = filePath;
    }

    public (bool RegistryData, bool Compact)? Get(string tabKey)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(tabKey);
        lock (_sync)
        {
            EnsureLoaded();
            return _modes!.TryGetValue(tabKey, out var mode) ? mode : null;
        }
    }

    public void Set(string tabKey, bool registryData, bool compact)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(tabKey);
        lock (_sync)
        {
            EnsureLoaded();
            _modes![tabKey] = (registryData, compact);
            Save();
        }
    }

    private void EnsureLoaded()
    {
        if (_modes is not null)
            return;

        _modes = new Dictionary<string, (bool, bool)>(StringComparer.OrdinalIgnoreCase);
        try
        {
            if (!File.Exists(_filePath))
                return;

            foreach (var line in File.ReadAllLines(_filePath))
            {
                var eq = line.IndexOf('=', StringComparison.Ordinal);
                if (eq <= 0)
                    continue;

                var key = line[..eq].Trim();
                var parts = line[(eq + 1)..].Split('|');
                if (key.Length == 0 || parts.Length != 2)
                    continue;

                _modes[key] = (parts[0].Trim() == "1", parts[1].Trim() == "1");
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Unreadable preferences degrade to defaults; never block the UI. But a
            // failed load must not let a later Set rewrite the file from an empty
            // dictionary and drop other tabs' prefs, so Save is disabled for this
            // instance (modes still work in memory).
            _loadFailed = true;
        }
    }

    private void Save()
    {
        if (_loadFailed)
            return;

        try
        {
            var directory = Path.GetDirectoryName(_filePath);
            if (!string.IsNullOrEmpty(directory))
                Directory.CreateDirectory(directory);

            File.WriteAllLines(_filePath, _modes!
                .OrderBy(kv => kv.Key, StringComparer.OrdinalIgnoreCase)
                .Select(kv => $"{kv.Key}={(kv.Value.RegistryData ? "1" : "0")}|{(kv.Value.Compact ? "1" : "0")}"));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Persistence is best-effort; the in-memory mode still applies.
        }
    }
}
