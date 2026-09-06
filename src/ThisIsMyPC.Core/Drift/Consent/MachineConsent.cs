using System.Text.Json;
using ThisIsMyPC.Core.Coordination;

namespace ThisIsMyPC.Core.Drift.Consent;

public enum MachineConsentStatus { Loaded, Missing, Corrupt, Unsupported, Untrusted, Unavailable, LeaseRequired }

/// <summary>A read result authorizes consent only when a trusted, supported document explicitly enables it.</summary>
public sealed record MachineConsentState(MachineConsentStatus Status, bool Enabled, string Detail, DateTimeOffset? ChangedAtUtc = null)
{
    public bool IsGranted => Status == MachineConsentStatus.Loaded && Enabled;
    public static MachineConsentState Off(MachineConsentStatus status, string detail) => new(status, false, detail);
}

/// <summary>Successful writes returned only after durable replacement. Failure never reports consent as granted.</summary>
public sealed record MachineConsentWriteResult(bool IsSuccess, MachineConsentState State);

/// <summary>Machine-scoped explicit consent. Reads never create files; writes require a held mutation lease, and enabling also requires recovery.</summary>
public interface IMachineConsentStore
{
    MachineConsentState Read();
    MachineConsentWriteResult SetEnabled(bool enabled, IMutationLease lease);
}

/// <summary>Bounded strict document format, independent of file trust and persistence.</summary>
public static class MachineConsentDocument
{
    public const int MaximumBytes = 4096;
    public const int Version = 1;

    public static MachineConsentState Parse(ReadOnlyMemory<byte> bytes, DateTimeOffset now)
    {
        if (bytes.Length is 0 or > MaximumBytes)
            return MachineConsentState.Off(MachineConsentStatus.Corrupt, "Consent document size is invalid.");
        try
        {
            using var document = JsonDocument.Parse(bytes, new JsonDocumentOptions { MaxDepth = 4 });
            if (document.RootElement.ValueKind != JsonValueKind.Object)
                return Invalid();
            var names = new HashSet<string>(StringComparer.Ordinal);
            int? version = null;
            bool? enabled = null;
            DateTimeOffset? changed = null;
            foreach (var property in document.RootElement.EnumerateObject())
            {
                if (!names.Add(property.Name))
                    return Invalid();
                switch (property.Name)
                {
                    case "version" when property.Value.TryGetInt32(out var parsed): version = parsed; break;
                    case "enabled" when property.Value.ValueKind is JsonValueKind.True or JsonValueKind.False:
                        enabled = property.Value.GetBoolean(); break;
                    case "changedAtUtc" when property.Value.ValueKind == JsonValueKind.String
                        && property.Value.TryGetDateTimeOffset(out var date): changed = date; break;
                    default: return Invalid();
                }
            }
            if (version is not null && version != Version)
                return MachineConsentState.Off(MachineConsentStatus.Unsupported, "Consent schema version is unsupported.");
            if (version is null || enabled is null || changed is null || changed > now || changed.Value.Offset != TimeSpan.Zero)
                return Invalid();
            return new(MachineConsentStatus.Loaded, enabled.Value, "Explicit machine consent loaded.", changed);
        }
        catch (Exception ex) when (ex is JsonException or InvalidOperationException or FormatException)
        {
            return Invalid();
        }

        static MachineConsentState Invalid() => MachineConsentState.Off(MachineConsentStatus.Corrupt, "Consent document is malformed or dated in the future.");
    }

    public static byte[] Encode(bool enabled, DateTimeOffset changedAtUtc)
    {
        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream))
        {
            writer.WriteStartObject();
            writer.WriteNumber("version", Version);
            writer.WriteBoolean("enabled", enabled);
            writer.WriteString("changedAtUtc", changedAtUtc.ToUniversalTime());
            writer.WriteEndObject();
        }
        return stream.ToArray();
    }
}