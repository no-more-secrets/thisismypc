using System.Text.Json;
using ThisIsMyPC.Core.Changes;
using ThisIsMyPC.Core.Data;

namespace ThisIsMyPC.Core.Drift;

public interface IDriftBaselineStore
{
    /// <summary>Merges successfully-applied descriptors into the baseline (AfterValue becomes the expectation).</summary>
    void RecordApplied(IEnumerable<ChangeDescriptor> applied);
}

/// <summary>
/// Last-known-applied state for the drift watchdog (28-3). Written by the app after
/// every successful apply/undo/redo; read by the Session 0 service at boot. Lives in
/// ProgramData so SYSTEM can reach it without resolving a user profile. Registry
/// value types only; service start types, scheduled tasks and power settings have
/// their own authoritative stores the watchdog does not model yet.
/// Persistence is best-effort: a failed write never fails the apply that triggered it.
/// </summary>
public sealed class DriftBaselineStore : IDriftBaselineStore
{
    public const string FileName = "drift-baseline.json";

    private readonly string _path;
    private readonly string? _userSid;
    private readonly Lock _sync = new();

    public DriftBaselineStore(string? path = null, string? userSid = null)
    {
        _path = path ?? Path.Combine(AppConstants.DataDirectoryPath, FileName);
        _userSid = userSid;
    }

    public static bool IsTrackable(ChangeValueType valueType) => valueType is
        ChangeValueType.Registry_String or
        ChangeValueType.Registry_DWord or
        ChangeValueType.Registry_ExpandString or
        ChangeValueType.Registry_Binary or
        ChangeValueType.Registry_MultiString;

    public void RecordApplied(IEnumerable<ChangeDescriptor> applied)
    {
        ArgumentNullException.ThrowIfNull(applied);
        var trackable = applied.Where(c => IsTrackable(c.ValueType)).ToList();
        if (trackable.Count == 0)
            return;

        lock (_sync)
        {
            var entries = (Load(_path)?.Entries ?? [])
                .ToDictionary(e => e.SystemLocation, StringComparer.OrdinalIgnoreCase);

            var now = DateTimeOffset.UtcNow;
            foreach (var change in trackable)
            {
                entries[change.SystemLocation] = new DriftBaselineEntry
                {
                    ModuleId = change.ModuleId,
                    SettingId = change.SettingId,
                    DisplayName = change.DisplayName,
                    SystemLocation = change.SystemLocation,
                    ValueType = change.ValueType,
                    ExpectedValue = change.AfterValue ?? string.Empty,
                    EnforcementJson = EnforcementJson.Serialize(change.Enforcement),
                    UpdatedAtUtc = now,
                };
            }

            Save(new DriftBaselineDocument { UserSid = _userSid, Entries = [.. entries.Values] });
        }
    }

    /// <summary>Null when absent/corrupt; the watchdog treats that as "no baseline yet".</summary>
    public static DriftBaselineDocument? Load(string path)
    {
        try
        {
            if (!File.Exists(path))
                return null;
            return JsonSerializer.Deserialize(
                File.ReadAllText(path), DriftJsonContext.Default.DriftBaselineDocument);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            return null;
        }
    }

    private void Save(DriftBaselineDocument document)
    {
        try
        {
            var directory = Path.GetDirectoryName(_path);
            if (!string.IsNullOrEmpty(directory))
                Directory.CreateDirectory(directory);
            var json = JsonSerializer.Serialize(document, DriftJsonContext.Default.DriftBaselineDocument);
            var tempPath = _path + ".tmp";
            File.WriteAllText(tempPath, json);
            File.Move(tempPath, _path, overwrite: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Best-effort; the mutation that triggered this already succeeded.
        }
    }
}
