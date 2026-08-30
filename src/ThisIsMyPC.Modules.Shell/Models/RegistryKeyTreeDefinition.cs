using System.Text.Json;
using System.Text.Json.Serialization;

namespace ThisIsMyPC.Modules.Shell.Models;

/// <summary>One value inside a key tree. Binary data is base64 in <see cref="Data"/>.</summary>
public sealed record RegistryKeyTreeValue(
    string SubPath,
    string Name,
    RegistryKeyTreeValueKind Kind,
    string Data);

public enum RegistryKeyTreeValueKind
{
    String,
    ExpandString,
    Binary,
}

/// <summary>
/// A small registry key tree materialized or deleted as one change (curated
/// context-menu entries like the MSI Extract verb). The whole definition
/// serializes into the descriptor's AfterValue; the SystemLocation is the root
/// key path. Deletion is always recursive on that root. The module only accepts
/// these for allowlisted root paths.
/// </summary>
public sealed record RegistryKeyTreeDefinition
{
    public required IReadOnlyList<RegistryKeyTreeValue> Values { get; init; }

    public string Serialize() =>
        JsonSerializer.Serialize(this, RegistryKeyTreeJsonContext.Default.RegistryKeyTreeDefinition);

    public static RegistryKeyTreeDefinition? Deserialize(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
            return null;
        try
        {
            return JsonSerializer.Deserialize(json, RegistryKeyTreeJsonContext.Default.RegistryKeyTreeDefinition);
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
    UseStringEnumConverter = true)]
[JsonSerializable(typeof(RegistryKeyTreeDefinition))]
public sealed partial class RegistryKeyTreeJsonContext : JsonSerializerContext;
