using ThisIsMyPC.Modules.Startup.Models;

namespace ThisIsMyPC.Modules.Startup.Services;

/// <summary>
/// Persists user classification overrides as one "taskPath|Classification"
/// line per task (no serializer needed — NativeAOT-friendly, human-readable).
/// Reads are cached; writes rewrite the whole file (the set is tiny).
/// </summary>
public sealed class TaskClassificationOverrideStore
{
    private readonly string _filePath;
    private readonly Lock _gate = new();
    private Dictionary<string, TaskClassification>? _cache;

    public TaskClassificationOverrideStore(string filePath)
    {
        _filePath = filePath;
    }

    public TaskClassification? Get(string taskPath)
    {
        lock (_gate)
        {
            EnsureLoaded();
            return _cache!.TryGetValue(taskPath, out var classification) ? classification : null;
        }
    }

    public void Set(string taskPath, TaskClassification classification)
    {
        lock (_gate)
        {
            EnsureLoaded();
            _cache![taskPath] = classification;
            Save();
        }
    }

    public void Remove(string taskPath)
    {
        lock (_gate)
        {
            EnsureLoaded();
            if (_cache!.Remove(taskPath))
                Save();
        }
    }

    private void EnsureLoaded()
    {
        if (_cache is not null)
            return;

        _cache = new Dictionary<string, TaskClassification>(StringComparer.OrdinalIgnoreCase);
        try
        {
            if (!File.Exists(_filePath))
                return;
            foreach (var line in File.ReadAllLines(_filePath))
            {
                var separator = line.LastIndexOf('|');
                if (separator <= 0)
                    continue;
                var path = line[..separator];
                if (Enum.TryParse<TaskClassification>(line[(separator + 1)..], out var classification) &&
                    Enum.IsDefined(classification))
                {
                    _cache[path] = classification;
                }
            }
        }
        catch
        {
            // Unreadable store — start empty; next Set() rewrites it.
        }
    }

    private void Save()
    {
        try
        {
            var directory = Path.GetDirectoryName(_filePath);
            if (!string.IsNullOrEmpty(directory))
                Directory.CreateDirectory(directory);
            File.WriteAllLines(_filePath, _cache!.Select(kv => $"{kv.Key}|{kv.Value}"));
        }
        catch
        {
            // Persistence is best-effort; the in-memory override still applies this session.
        }
    }
}
