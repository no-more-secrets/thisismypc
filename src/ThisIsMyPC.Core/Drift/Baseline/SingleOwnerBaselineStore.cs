using System.Text.Json;
using System.Text.Json.Serialization;
using ThisIsMyPC.Core.Coordination;
using ThisIsMyPC.Core.Services;

namespace ThisIsMyPC.Core.Drift.Baseline;

/// <summary>
/// Trusted adapter contract, not a path trust assertion. Reads distinguish missing from failure.
/// Implementations verify ownership, DACLs and reparse-safe ancestry and hold protection through use.
/// ReplaceDurably returns only after atomic replacement and required storage flushes complete.
/// Failures throw with original diagnostics. An ambiguous failure may have committed; callers must reread.
/// No production adapter is supplied by Core.
/// </summary>
public interface ITrustedBaselineStorage
{
    byte[]? Read(int maximumBytes);
    void ReplaceDurably(ReadOnlyMemory<byte> document);
}

public sealed record SingleOwnerBaselineEntry(string ModuleId, string SettingId, string CanonicalLocation,
    RegistryValueData Expected, DateTimeOffset UpdatedAtUtc);

internal sealed record SingleOwnerBaselineDocument(int Version, string PrimaryUserSid, SingleOwnerBaselineEntry[] Entries);
[JsonSourceGenerationOptions(UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow)]
[JsonSerializable(typeof(SingleOwnerBaselineDocument))]
internal sealed partial class SingleOwnerBaselineJsonContext : JsonSerializerContext;

/// <summary>Separate from the legacy best-effort baseline. All operations require the caller's machine lease.</summary>
public sealed class SingleOwnerBaselineStore
{
    public const int MaximumBytes = 1024 * 1024;
    public static int MaximumEntries => RestorationCatalog.Default.Targets.Length;
    private readonly ITrustedBaselineStorage _storage;
    private readonly string _leaseName;
    private readonly string _primaryUserSid;

    public SingleOwnerBaselineStore(ITrustedBaselineStorage storage, string leaseName, string primaryUserSid)
    {
        ArgumentNullException.ThrowIfNull(storage);
        ArgumentException.ThrowIfNullOrWhiteSpace(leaseName);
        if (!RestorationCatalog.IsAccountSid(primaryUserSid))
            throw new ArgumentException("A supported primary account SID is required.", nameof(primaryUserSid));
        _primaryUserSid = primaryUserSid;
        _storage = storage;
        _leaseName = leaseName;
    }

    public string PrimaryUserSid => _primaryUserSid;

    public IReadOnlyList<SingleOwnerBaselineEntry> Read(IMutationLease lease)
    {
        CheckLease(lease, false);
        var bytes = _storage.Read(MaximumBytes);
        if (bytes is null) return Array.Empty<SingleOwnerBaselineEntry>();
        if (bytes.Length > MaximumBytes) throw new InvalidDataException("Baseline exceeds storage limit.");
        SingleOwnerBaselineDocument document;
        try
        {
            using var parsed = JsonDocument.Parse(bytes, new JsonDocumentOptions { MaxDepth = 8 });
            RejectDuplicateProperties(parsed.RootElement);
            document = JsonSerializer.Deserialize(bytes, SingleOwnerBaselineJsonContext.Default.SingleOwnerBaselineDocument)
                ?? throw new InvalidDataException("Baseline document is null.");
        }
        catch (JsonException ex) { throw new InvalidDataException("Baseline document is corrupt.", ex); }
        if (document.Version != 1 || document.Entries is null || !string.Equals(document.PrimaryUserSid, _primaryUserSid, StringComparison.Ordinal))
            throw new InvalidDataException("Unsupported baseline document.");
        return Validate(document.Entries).Values.ToArray();
    }

    /// <summary>Atomically merges a fully validated batch. Failure is never reported as success or treated as missing.</summary>
    public void RecordApplied(IReadOnlyList<SingleOwnerBaselineEntry> entries, IMutationLease lease)
    {
        ArgumentNullException.ThrowIfNull(entries);
        CheckLease(lease, true);
        var updates = Validate(entries);
        var merged = Validate(Read(lease));
        foreach (var pair in updates) merged[pair.Key] = pair.Value;
        if (merged.Count > MaximumEntries) throw new InvalidDataException("Baseline entry limit exceeded.");
        Persist(merged.Values, lease);
    }

    /// <summary>Durably removes affected choices before deliberate writes. Other choices remain unchanged.</summary>
    public void Remove(IReadOnlyCollection<string> canonicalLocations, IMutationLease lease)
    {
        ArgumentNullException.ThrowIfNull(canonicalLocations);
        CheckLease(lease, true);
        var entries = Validate(Read(lease));
        var locations = new HashSet<string>(canonicalLocations, StringComparer.OrdinalIgnoreCase);
        Persist(entries.Values.Where(entry => !locations.Contains(entry.CanonicalLocation)), lease);
    }

    private void Persist(IEnumerable<SingleOwnerBaselineEntry> entries, IMutationLease lease)
    {
        var document = new SingleOwnerBaselineDocument(1, _primaryUserSid, entries
            .OrderBy(e => e.CanonicalLocation, StringComparer.Ordinal).ToArray());
        var bytes = JsonSerializer.SerializeToUtf8Bytes(document, SingleOwnerBaselineJsonContext.Default.SingleOwnerBaselineDocument);
        if (bytes.Length > MaximumBytes) throw new InvalidDataException("Baseline exceeds storage limit.");
        CheckLease(lease, true);
        _storage.ReplaceDurably(bytes);
    }

    private Dictionary<string, SingleOwnerBaselineEntry> Validate(IReadOnlyList<SingleOwnerBaselineEntry> entries)
    {
        if (entries.Count > MaximumEntries) throw new InvalidDataException("Baseline entry limit exceeded.");
        var result = new Dictionary<string, SingleOwnerBaselineEntry>(StringComparer.Ordinal);
        foreach (var entry in entries)
        {
            if (entry is null || entry.Expected is null || entry.UpdatedAtUtc == default)
                throw new InvalidDataException("Baseline entry is incomplete.");
            var validation = RestorationCatalog.Default.Validate(new DriftBaselineEntry
            {
                ModuleId = entry.ModuleId, SettingId = entry.SettingId, DisplayName = entry.SettingId,
                SystemLocation = entry.CanonicalLocation, ValueType = Changes.ChangeValueType.Registry_DWord,
                ExpectedValue = entry.Expected.Data, UpdatedAtUtc = entry.UpdatedAtUtc,
            }, _primaryUserSid);
            if (!validation.IsAccepted || validation.Candidate!.DesiredValue != entry.Expected)
                throw new InvalidDataException("Invalid baseline entry: " + validation.Detail);
            var candidate = validation.Candidate!;
            var canonical = entry with { CanonicalLocation = candidate.Target.KeyPath + "\\" + candidate.Target.ValueName };
            if (!result.TryAdd(canonical.CanonicalLocation, canonical))
                throw new InvalidDataException("Duplicate baseline identity.");
        }
        return result;
    }

    private static void RejectDuplicateProperties(JsonElement element)
    {
        if (element.ValueKind == JsonValueKind.Object)
        {
            var names = new HashSet<string>(StringComparer.Ordinal);
            foreach (var property in element.EnumerateObject())
            {
                if (!names.Add(property.Name)) throw new InvalidDataException("Duplicate baseline property.");
                RejectDuplicateProperties(property.Value);
            }
        }
        else if (element.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in element.EnumerateArray()) RejectDuplicateProperties(item);
        }
    }

    private void CheckLease(IMutationLease lease, bool writing)
    {
        ArgumentNullException.ThrowIfNull(lease);
        if (!lease.IsHeld || (writing && !lease.CanWrite) || !string.Equals(lease.Name, _leaseName, StringComparison.Ordinal))
            throw new InvalidOperationException("A matching held baseline lease is required; writes also require recovery.");
    }
}
