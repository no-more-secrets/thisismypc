using System.Text.Json;
using System.Text.Json.Serialization;

namespace ThisIsMyPC.Ipc.Contracts;

/// <summary>
/// Every frame carries one envelope. <see cref="Nonce"/> is generated per request by
/// the client and must be echoed verbatim in the response; a reply with a stale or
/// foreign nonce is discarded (replay guard). <see cref="PayloadJson"/> is the
/// message-type-specific DTO, serialized separately so the envelope shape never
/// changes when message types are added (PawnIO brokering extensibility).
/// </summary>
public sealed record IpcEnvelope
{
    public required string Type { get; init; }
    public required string Nonce { get; init; }
    public string? PayloadJson { get; init; }
}

public static class IpcMessageTypes
{
    public const string Ping = "ping";
    public const string ServiceStatus = "service-status";
    public const string DriftReport = "drift-report";
    public const string Error = "error";
}

public sealed record ServiceStatusResponse
{
    public required int ProtocolVersion { get; init; }
    public required string ServiceVersion { get; init; }
    public required DateTimeOffset StartedAtUtc { get; init; }
    public required bool BaselinePresent { get; init; }
    public DateTimeOffset? LastDriftScanUtc { get; init; }
}

/// <summary>One reverted setting in a drift report (28-3).</summary>
public sealed record DriftItem
{
    public required string ModuleId { get; init; }
    public required string SettingId { get; init; }
    public required string DisplayName { get; init; }
    public required string SystemLocation { get; init; }
    public required string ValueType { get; init; }
    /// <summary>What ThisIsMyPC applied.</summary>
    public required string ExpectedValue { get; init; }
    /// <summary>What the system reverted it to.</summary>
    public required string CurrentValue { get; init; }
    public string? SuspectedCause { get; init; }
    /// <summary>Serialized SettingEnforcement so reapply keeps enforcement routing.</summary>
    public string? EnforcementJson { get; init; }
}

public sealed record DriftReportResponse
{
    public required bool BaselinePresent { get; init; }
    public DateTimeOffset? GeneratedAtUtc { get; init; }
    public IReadOnlyList<DriftItem> Items { get; init; } = [];
}

public sealed record IpcErrorResponse
{
    public required string Message { get; init; }
}

[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    PropertyNameCaseInsensitive = true,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull)]
[JsonSerializable(typeof(IpcEnvelope))]
[JsonSerializable(typeof(ServiceStatusResponse))]
[JsonSerializable(typeof(DriftReportResponse))]
[JsonSerializable(typeof(IpcErrorResponse))]
public sealed partial class IpcJsonContext : JsonSerializerContext;

public static class IpcSerializer
{
    public static byte[] SerializeEnvelope(IpcEnvelope envelope) =>
        JsonSerializer.SerializeToUtf8Bytes(envelope, IpcJsonContext.Default.IpcEnvelope);

    public static IpcEnvelope? DeserializeEnvelope(byte[] frame)
    {
        try
        {
            return JsonSerializer.Deserialize(frame, IpcJsonContext.Default.IpcEnvelope);
        }
        catch (JsonException)
        {
            return null;
        }
    }
}
