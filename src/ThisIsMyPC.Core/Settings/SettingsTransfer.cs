using System.Text.Json;
using System.Text.Json.Serialization;

namespace ThisIsMyPC.Core.Settings;

/// <summary>JSON shape of an exported settings file (7-4).</summary>
public sealed class SettingsExportDocument
{
    public string? ExportedAt { get; set; }
    public string? AppVersion { get; set; }
    public string? MachineName { get; set; }
    public Dictionary<string, string>? AppSettings { get; set; }
    public Dictionary<string, Dictionary<string, string>>? ModuleSettings { get; set; }
}

[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    PropertyNameCaseInsensitive = true,
    ReadCommentHandling = JsonCommentHandling.Skip,
    AllowTrailingCommas = true,
    WriteIndented = true)]
[JsonSerializable(typeof(SettingsExportDocument))]
public sealed partial class SettingsExportJsonContext : JsonSerializerContext;

public sealed record SettingsImportRow(
    string Scope,          // SettingChangedEventArgs.AppScope or a module id
    string Key,
    string? CurrentValue,
    string ImportedValue,
    string? SkipReason)
{
    public bool WillApply => SkipReason is null;
}

public sealed record SettingsImportPreview(
    IReadOnlyList<SettingsImportRow> Rows,
    string? SourceMachineName,
    string? ExportedAt)
{
    public int ApplicableCount => Rows.Count(r => r.WillApply);
    public int SkippedCount => Rows.Count(r => !r.WillApply);
}

/// <summary>
/// Builds, parses, previews, and applies settings export files. Pure functions over
/// ISettingsService; file I/O and dialogs stay in the App layer.
/// </summary>
public static class SettingsTransfer
{
    public static string BuildExportJson(ISettingsService settings, string appVersion, string machineName)
    {
        ArgumentNullException.ThrowIfNull(settings);

        var document = new SettingsExportDocument
        {
            ExportedAt = DateTimeOffset.UtcNow.ToString("O"),
            AppVersion = appVersion,
            MachineName = machineName,
            AppSettings = new Dictionary<string, string>(settings.SnapshotApp(), StringComparer.Ordinal),
            ModuleSettings = settings.SnapshotModules().ToDictionary(
                kv => kv.Key,
                kv => new Dictionary<string, string>(kv.Value, StringComparer.Ordinal),
                StringComparer.Ordinal),
        };

        return JsonSerializer.Serialize(document, SettingsExportJsonContext.Default.SettingsExportDocument);
    }

    public static string DefaultExportFileName(DateTimeOffset now) =>
        $"thisismypc-settings-{now:yyyy-MM-dd}.json";

    /// <summary>Null when the file is not a valid export document.</summary>
    public static SettingsExportDocument? Parse(string json)
    {
        try
        {
            var document = JsonSerializer.Deserialize(json, SettingsExportJsonContext.Default.SettingsExportDocument);
            return document is { AppSettings: not null } or { ModuleSettings: not null } ? document : null;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    /// <summary>
    /// Current-vs-imported rows. App-scope keys always apply (harmless strings, forward
    /// compatible); module scopes apply only when the module is installed here.
    /// </summary>
    public static SettingsImportPreview BuildPreview(
        ISettingsService settings,
        SettingsExportDocument document,
        IReadOnlyCollection<string> installedModuleIds)
    {
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(installedModuleIds);

        var installed = new HashSet<string>(installedModuleIds, StringComparer.Ordinal);
        var rows = new List<SettingsImportRow>();
        var currentApp = settings.SnapshotApp();

        foreach (var (key, value) in document.AppSettings ?? [])
        {
            rows.Add(new SettingsImportRow(
                SettingChangedEventArgs.AppScope, key,
                currentApp.TryGetValue(key, out var current) ? current : null,
                value, SkipReason: null));
        }

        foreach (var (moduleId, values) in document.ModuleSettings ?? [])
        {
            var skip = installed.Contains(moduleId)
                ? null
                : $"{moduleId} is not available on this system";
            foreach (var (key, value) in values)
            {
                rows.Add(new SettingsImportRow(
                    moduleId, key, settings.GetModule(moduleId, key), value, skip));
            }
        }

        return new SettingsImportPreview(rows, document.MachineName, document.ExportedAt);
    }

    /// <summary>Applies every applicable row; returns (applied, skipped).</summary>
    public static (int Applied, int Skipped) Apply(ISettingsService settings, SettingsImportPreview preview)
    {
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentNullException.ThrowIfNull(preview);

        var applied = 0;
        foreach (var row in preview.Rows.Where(r => r.WillApply))
        {
            if (row.Scope == SettingChangedEventArgs.AppScope)
                settings.SetApp(row.Key, row.ImportedValue);
            else
                settings.SetModule(row.Scope, row.Key, row.ImportedValue);
            applied++;
        }

        return (applied, preview.SkippedCount);
    }
}
