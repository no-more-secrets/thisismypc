using System.Text.Json;
using System.Text.Json.Serialization;
using ThisIsMyPC.Core.Modules;

namespace ThisIsMyPC.Core.Sets.Serialization;

// DTO layer for set JSON files: everything nullable, validated during mapping in
// SetProvider so a missing field produces a warning instead of a serializer throw.

public sealed record SetDocument
{
    public string? Name { get; init; }
    public string? Description { get; init; }
    public SetCategory? Category { get; init; }
    public string? Version { get; init; }
    public string? Author { get; init; }
    public IReadOnlyList<SetEntryDocument>? Entries { get; init; }
}

public sealed record SetEntryDocument
{
    public string? ModuleId { get; init; }
    public string? SettingId { get; init; }
    public string? Value { get; init; }
    public string? Description { get; init; }
    public string? DisplayValue { get; init; }
    public string? Group { get; init; }
    public SetEnforcementDocument? Enforcement { get; init; }
}

public sealed record SetEnforcementDocument
{
    public IReadOnlyList<string>? CompanionServices { get; init; }
    public IReadOnlyList<string>? CompanionTasks { get; init; }
    public IReadOnlyList<string>? GpCacheEntries { get; init; }
    public IReadOnlyList<string>? ReversionVectors { get; init; }
    public WindowsSku? SkuRestriction { get; init; }
    public bool OwnerModeRequired { get; init; }
    public bool AclElevation { get; init; }
    public bool RestoresCompanions { get; init; }
}

[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    PropertyNameCaseInsensitive = true,
    UseStringEnumConverter = true,
    ReadCommentHandling = JsonCommentHandling.Skip,
    AllowTrailingCommas = true,
    WriteIndented = true,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull)]
[JsonSerializable(typeof(SetDocument))]
[JsonSerializable(typeof(SetEnforcementDocument))]
public sealed partial class SetJsonContext : JsonSerializerContext;
