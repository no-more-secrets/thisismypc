using System.Text.Json;
using System.Text.Json.Serialization;

namespace ThisIsMyPC.Modules.Shell.Models;

/// <summary>
/// A user-authored context menu entry (2-6). The full definition serializes into a
/// single ChangeDescriptor value, and the module materializes/removes the verb key
/// tree from it. Entries live only under the HKCU classes overlay and are namespaced
/// with the <see cref="VerbKeyPrefix"/> so ThisIsMyPC never touches foreign verbs.
/// </summary>
public sealed record CustomVerbDefinition
{
    /// <summary>Verb key name prefix that marks an entry as ThisIsMyPC-owned.</summary>
    public const string VerbKeyPrefix = "ThisIsMyPC.";

    /// <summary>Registry scope segment under HKCU\Software\Classes (e.g. "*", "Directory").</summary>
    public required string Scope { get; init; }

    /// <summary>Stable key-name suffix; full key name is VerbKeyPrefix + VerbId.</summary>
    public required string VerbId { get; init; }

    /// <summary>Display text in the context menu (verb key default value).</summary>
    public required string Label { get; init; }

    /// <summary>Command line executed on click (command subkey default value).</summary>
    public required string Command { get; init; }

    /// <summary>Optional icon path ("Icon" value; absent when null).</summary>
    public string? IconPath { get; init; }

    /// <summary>Scopes offered by the creation form: registry segment + display name.</summary>
    public static IReadOnlyList<(string Scope, string DisplayName)> ScopeOptions { get; } =
    [
        ("*", "All files"),
        ("Directory", "Folders"),
        (@"Directory\Background", "Folder background"),
        ("DesktopBackground", "Desktop background"),
        ("Drive", "Drives"),
    ];

    public string KeyPath => $@"HKCU\Software\Classes\{Scope}\shell\{VerbKeyPrefix}{VerbId}";

    public string ScopeDisplayName =>
        ScopeOptions.FirstOrDefault(o => o.Scope.Equals(Scope, StringComparison.OrdinalIgnoreCase)).DisplayName
        ?? Scope;

    public string Serialize() =>
        JsonSerializer.Serialize(this, CustomVerbJsonContext.Default.CustomVerbDefinition);

    public static CustomVerbDefinition? Deserialize(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
            return null;
        try
        {
            return JsonSerializer.Deserialize(json, CustomVerbJsonContext.Default.CustomVerbDefinition);
        }
        catch (JsonException)
        {
            return null;
        }
    }
}

[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    PropertyNameCaseInsensitive = true,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull)]
[JsonSerializable(typeof(CustomVerbDefinition))]
public sealed partial class CustomVerbJsonContext : JsonSerializerContext;
