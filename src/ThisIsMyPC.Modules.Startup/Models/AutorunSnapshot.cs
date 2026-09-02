using System.Text.Json;
using System.Text.Json.Serialization;
using ThisIsMyPC.Core.Services;

namespace ThisIsMyPC.Modules.Startup.Models;

/// <summary>One value inside a snapshotted key tree; SubPath is relative to the item's key ("" for the key itself).</summary>
public sealed record AutorunSnapshotValue(string SubPath, string Name, RegistryValueData Value);

/// <summary>
/// The live copy of an autostart item that re-registered itself beside its
/// parked twin, captured at scan time so that purging it is undoable: a
/// registry value, a whole subkey tree, or a Startup file's bytes. Rides in
/// the descriptor's BeforeValue after the state word.
/// </summary>
public sealed record AutorunSnapshot
{
    /// <summary>Startup files above this size are not snapshotted (an exe dropped in the folder); the purge is then refused.</summary>
    public const int MaxFileBytes = 1024 * 1024;

    public required AutorunItemKind Kind { get; init; }
    public IReadOnlyList<AutorunSnapshotValue> Values { get; init; } = [];
    public string? FileBase64 { get; init; }

    public string Serialize() => JsonSerializer.Serialize(this, AutorunSnapshotJsonContext.Default.AutorunSnapshot);

    public static AutorunSnapshot? Deserialize(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
            return null;
        try
        {
            return JsonSerializer.Deserialize(json, AutorunSnapshotJsonContext.Default.AutorunSnapshot);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    /// <summary>Captures the live copy at its enabled location; null when it cannot be read whole.</summary>
    public static AutorunSnapshot? Capture(IRegistryService registry, IStartupFolderService folders, AutorunItemKind kind, string location, string name)
    {
        ArgumentNullException.ThrowIfNull(registry);
        switch (kind)
        {
            case AutorunItemKind.RegistryValue:
            {
                var read = registry.ReadValue(location, name);
                return read.IsSuccess && read.Value is not null
                    ? new AutorunSnapshot { Kind = kind, Values = [new("", name, read.Value)] }
                    : null;
            }
            case AutorunItemKind.RegistryKey:
            {
                var values = new List<AutorunSnapshotValue>();
                return CollectTree(registry, $@"{location}\{name}", "", values)
                    ? new AutorunSnapshot { Kind = kind, Values = values }
                    : null;
            }
            case AutorunItemKind.StartupFile:
            {
                var bytes = folders.ReadAllBytes(Path.Combine(location, name), MaxFileBytes);
                return bytes.IsSuccess && bytes.Value is not null
                    ? new AutorunSnapshot { Kind = kind, FileBase64 = Convert.ToBase64String(bytes.Value) }
                    : null;
            }
            default:
                return null;
        }
    }

    private static bool CollectTree(IRegistryService registry, string keyPath, string relative, List<AutorunSnapshotValue> values)
    {
        if (registry.EnumerateValues(keyPath) is { IsSuccess: true, Value: { } names })
        {
            foreach (var valueName in names)
            {
                var read = registry.ReadValue(keyPath, valueName);
                if (!read.IsSuccess || read.Value is null)
                    return false;
                values.Add(new(relative, valueName, read.Value));
            }
        }
        if (registry.EnumerateSubKeys(keyPath) is { IsSuccess: true, Value: { } subKeys })
        {
            foreach (var subKey in subKeys)
            {
                if (!CollectTree(registry, $@"{keyPath}\{subKey}", relative.Length == 0 ? subKey : $@"{relative}\{subKey}", values))
                    return false;
            }
        }
        return true;
    }
}

[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    PropertyNameCaseInsensitive = true,
    UseStringEnumConverter = true)]
[JsonSerializable(typeof(AutorunSnapshot))]
public sealed partial class AutorunSnapshotJsonContext : JsonSerializerContext;
