using System.Text.Json.Serialization;
using ThisIsMyPC.Core.Changes;

namespace ThisIsMyPC.Core.Drift;

/// <summary>
/// One expected system value in the drift baseline (28-3): the last state
/// ThisIsMyPC put a location into. Keyed by SystemLocation — the newest write to a
/// location wins regardless of which module or set produced it.
/// </summary>
public sealed record DriftBaselineEntry
{
    public required string ModuleId { get; init; }
    public required string SettingId { get; init; }
    public required string DisplayName { get; init; }
    public required string SystemLocation { get; init; }
    public required ChangeValueType ValueType { get; init; }
    public required string ExpectedValue { get; init; }
    /// <summary>Serialized SettingEnforcement (set-file DTO shape) so reapply keeps enforcement.</summary>
    public string? EnforcementJson { get; init; }
    public required DateTimeOffset UpdatedAtUtc { get; init; }
}

public sealed class DriftBaselineDocument
{
    public List<DriftBaselineEntry>? Entries { get; set; }
}

[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    PropertyNameCaseInsensitive = true,
    UseStringEnumConverter = true,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    WriteIndented = true)]
[JsonSerializable(typeof(DriftBaselineDocument))]
public sealed partial class DriftJsonContext : JsonSerializerContext;
