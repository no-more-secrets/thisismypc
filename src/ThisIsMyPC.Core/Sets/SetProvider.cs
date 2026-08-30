using System.Text.Json;
using ThisIsMyPC.Core.Enforcement;
using ThisIsMyPC.Core.Sets.Serialization;

namespace ThisIsMyPC.Core.Sets;

public sealed class SetProvider : ISetProvider
{
    private readonly string _builtInDirectory;
    private readonly string _userDirectory;

    public SetProvider(string builtInDirectory, string userDirectory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(builtInDirectory);
        ArgumentException.ThrowIfNullOrWhiteSpace(userDirectory);
        _builtInDirectory = builtInDirectory;
        _userDirectory = userDirectory;
    }

    public SetLoadResult LoadSets()
    {
        var sets = new List<SetDefinition>();
        var warnings = new List<string>();

        // A missing built-in directory means a broken install — worth a warning.
        // A missing user directory just means the user never created a set.
        LoadDirectory(_builtInDirectory, SetSource.BuiltIn, sets, warnings, warnIfMissing: true);
        LoadDirectory(_userDirectory, SetSource.User, sets, warnings, warnIfMissing: false);

        // Names are the set's identity in the browser (8.2) — duplicates load but get flagged.
        foreach (var duplicates in sets.GroupBy(s => s.Name, StringComparer.OrdinalIgnoreCase)
                     .Where(g => g.Count() > 1))
        {
            warnings.Add(
                $"Duplicate set name '{duplicates.Key}' in: {string.Join(", ", duplicates.Select(s => s.FilePath))}");
        }

        return new SetLoadResult { Sets = sets, Warnings = warnings };
    }

    private static void LoadDirectory(
        string directory, SetSource source, List<SetDefinition> sets, List<string> warnings, bool warnIfMissing)
    {
        string[] files;
        try
        {
            if (!Directory.Exists(directory))
            {
                if (warnIfMissing)
                    warnings.Add($"Built-in sets directory not found: {directory}");
                return;
            }
            files = Directory.GetFiles(directory, "*.json", SearchOption.TopDirectoryOnly);
        }
        catch (Exception ex)
        {
            warnings.Add($"Cannot enumerate sets directory '{directory}': {ex.Message}");
            return;
        }

        Array.Sort(files, StringComparer.OrdinalIgnoreCase);
        foreach (var file in files)
        {
            var definition = LoadFile(file, source, warnings);
            if (definition is not null)
                sets.Add(definition);
        }
    }

    private static SetDefinition? LoadFile(string file, SetSource source, List<string> warnings)
    {
        SetDocument? document;
        try
        {
            using var stream = File.OpenRead(file);
            document = JsonSerializer.Deserialize(stream, SetJsonContext.Default.SetDocument);
        }
        catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException)
        {
            warnings.Add($"Skipped set '{Path.GetFileName(file)}': {ex.Message}");
            return null;
        }

        if (document is null)
        {
            warnings.Add($"Skipped set '{Path.GetFileName(file)}': file contains JSON null.");
            return null;
        }

        return Map(document, file, source, warnings);
    }

    private static SetDefinition? Map(SetDocument document, string file, SetSource source, List<string> warnings)
    {
        var fileName = Path.GetFileName(file);
        var problems = new List<string>();

        if (string.IsNullOrWhiteSpace(document.Name))
            problems.Add("missing 'name'");
        if (string.IsNullOrWhiteSpace(document.Description))
            problems.Add("missing 'description'");
        // JsonStringEnumConverter accepts raw integers, so an out-of-range numeric value
        // ("category": 99) deserializes without error — reject it here.
        if (document.Category is null)
            problems.Add("missing 'category'");
        else if (!Enum.IsDefined(document.Category.Value))
            problems.Add($"unknown 'category' value {(int)document.Category.Value}");
        if (string.IsNullOrWhiteSpace(document.Version))
            problems.Add("missing 'version'");
        if (string.IsNullOrWhiteSpace(document.Author))
            problems.Add("missing 'author'");
        if (document.Entries is not { Count: > 0 })
            problems.Add("missing or empty 'entries'");

        var entries = new List<SetEntry>();
        for (var i = 0; i < (document.Entries?.Count ?? 0); i++)
        {
            var entry = document.Entries![i];
            // Value uses a plain null check: the empty string is a legitimate desired
            // value (e.g. blank registry default values).
            if (string.IsNullOrWhiteSpace(entry.ModuleId)
                || string.IsNullOrWhiteSpace(entry.SettingId)
                || entry.Value is null
                || string.IsNullOrWhiteSpace(entry.Description))
            {
                problems.Add($"entry {i} is missing moduleId, settingId, value, or description");
                continue;
            }

            if (entry.Enforcement?.SkuRestriction is { } sku && !Enum.IsDefined(sku))
            {
                problems.Add($"entry {i} has unknown 'skuRestriction' value {(int)sku}");
                continue;
            }

            entries.Add(new SetEntry
            {
                ModuleId = entry.ModuleId,
                SettingId = entry.SettingId,
                Value = entry.Value,
                Description = entry.Description,
                DisplayValue = entry.DisplayValue,
                Group = entry.Group,
                Enforcement = MapEnforcement(entry.Enforcement),
            });
        }

        if (problems.Count > 0)
        {
            warnings.Add($"Skipped set '{fileName}': {string.Join("; ", problems)}.");
            return null;
        }

        return new SetDefinition
        {
            Name = document.Name!,
            Description = document.Description!,
            Category = document.Category!.Value,
            Version = document.Version!,
            Author = document.Author!,
            Entries = entries,
            Source = source,
            FilePath = file,
        };
    }

    internal static SettingEnforcement? MapEnforcement(SetEnforcementDocument? document)
    {
        if (document is null)
            return null;

        return new SettingEnforcement
        {
            CompanionServices = document.CompanionServices,
            CompanionTasks = document.CompanionTasks,
            GPCacheEntries = document.GpCacheEntries,
            ReversionVectors = document.ReversionVectors,
            SkuRestriction = document.SkuRestriction,
            OwnerModeRequired = document.OwnerModeRequired,
            AclElevation = document.AclElevation,
            RestoresCompanions = document.RestoresCompanions,
        };
    }
}
