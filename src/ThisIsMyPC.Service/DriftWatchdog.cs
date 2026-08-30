using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using ThisIsMyPC.Core;
using ThisIsMyPC.Core.Changes;
using ThisIsMyPC.Core.Drift;
using ThisIsMyPC.Core.Services;
using ThisIsMyPC.Ipc.Contracts;

namespace ThisIsMyPC.Service;

/// <summary>
/// Post-reboot drift detection (28-3): at service start (which includes every boot
/// for an auto-start service) the baseline written by the desktop app is compared
/// against live registry state; mismatches become the drift report served over IPC.
/// Post-boot only; continuous event-driven detection is Phase 3 by design.
/// </summary>
public sealed class DriftWatchdog : IHostedService, IDriftReportSource
{
    private readonly IRegistryService _registry;
    private readonly ILogger<DriftWatchdog> _logger;
    private readonly string _baselinePath;
    private readonly Func<string, bool> _baselineTrustCheck;
    private readonly Lock _sync = new();

    private DriftReportResponse _report = new() { BaselinePresent = false };
    private DateTimeOffset? _lastScanUtc;

    public DriftWatchdog(
        IRegistryService registry,
        ILogger<DriftWatchdog> logger,
        string? baselinePath = null,
        Func<string, bool>? baselineTrustCheck = null)
    {
        _registry = registry;
        _logger = logger;
        _baselinePath = baselinePath
            ?? Path.Combine(AppConstants.MachineDataDirectoryPath, DriftBaselineStore.FileName);
        _baselineTrustCheck = baselineTrustCheck ?? IsOwnedByAdminsOrSystem;
    }

    /// <summary>
    /// ProgramData lets standard users pre-create files they then own (and an owner
    /// can always rewrite the DACL). A baseline not owned by SYSTEM/Administrators
    /// is untrusted input to a SYSTEM service and is refused outright.
    /// </summary>
    private static bool IsOwnedByAdminsOrSystem(string path)
    {
        try
        {
            var owner = (System.Security.Principal.SecurityIdentifier?)new FileInfo(path)
                .GetAccessControl()
                .GetOwner(typeof(System.Security.Principal.SecurityIdentifier));
            return owner is not null
                && (owner.IsWellKnown(System.Security.Principal.WellKnownSidType.LocalSystemSid)
                    || owner.IsWellKnown(System.Security.Principal.WellKnownSidType.BuiltinAdministratorsSid));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or SystemException)
        {
            return false;
        }
    }

    public bool BaselinePresent
    {
        get { lock (_sync) return _report.BaselinePresent; }
    }

    public DateTimeOffset? LastScanUtc
    {
        get { lock (_sync) return _lastScanUtc; }
    }

    public DriftReportResponse GetReport()
    {
        lock (_sync)
            return _report;
    }

    public Task StartAsync(CancellationToken cancellationToken)
    {
        try
        {
            ScanOnce();
        }
        catch (Exception ex)
        {
            // The watchdog failing must never keep the pipe server from starting.
            _logger.LogError(ex, "Drift scan failed");
        }
        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    /// <summary>One comparison pass (also the test seam).</summary>
    public void ScanOnce()
    {
        var now = DateTimeOffset.UtcNow;
        if (File.Exists(_baselinePath) && !_baselineTrustCheck(_baselinePath))
        {
            _logger.LogWarning("Drift baseline is not owned by SYSTEM/Administrators; refusing to read it");
            lock (_sync)
            {
                _report = new DriftReportResponse { BaselinePresent = false, GeneratedAtUtc = now };
                _lastScanUtc = now;
            }
            return;
        }

        var baseline = DriftBaselineStore.Load(_baselinePath);
        if (baseline?.Entries is not { Count: > 0 } entries)
        {
            lock (_sync)
            {
                _report = new DriftReportResponse { BaselinePresent = false, GeneratedAtUtc = now };
                _lastScanUtc = now;
            }
            return;
        }

        // HKCU under LocalSystem is S-1-5-18's hive; user entries must be read via
        // HKU\{sid}, and only when that profile hive is actually loaded (a boot-time
        // scan can run before logon; a missing hive is not drift).
        var userHive = baseline.UserSid is { Length: > 0 } sid ? $@"HKU\{sid}" : null;
        var userHiveLoaded = userHive is not null
            && _registry.KeyExists(userHive) is { IsSuccess: true, Value: true };

        var items = new List<DriftItem>();
        foreach (var entry in entries)
        {
            string location;
            if (entry.SystemLocation.StartsWith(@"HKCU\", StringComparison.OrdinalIgnoreCase))
            {
                if (!userHiveLoaded)
                    continue; // unverifiable, never a false positive
                location = $@"{userHive}\{entry.SystemLocation[5..]}";
            }
            else
            {
                location = entry.SystemLocation;
            }

            var current = ReadCurrent(entry, location);
            if (current is null)
                continue; // unreadable; never report drift on a failed probe
            if (Normalize(current) == Normalize(entry.ExpectedValue))
                continue;

            items.Add(new DriftItem
            {
                ModuleId = entry.ModuleId,
                SettingId = entry.SettingId,
                DisplayName = entry.DisplayName,
                SystemLocation = entry.SystemLocation,
                ValueType = entry.ValueType.ToString(),
                ExpectedValue = entry.ExpectedValue,
                CurrentValue = current,
                SuspectedCause = SuspectCause(entry.SystemLocation),
                EnforcementJson = entry.EnforcementJson,
            });
        }

        lock (_sync)
        {
            _report = new DriftReportResponse
            {
                BaselinePresent = true,
                GeneratedAtUtc = now,
                Items = items,
            };
            _lastScanUtc = now;
        }
        _logger.LogInformation("Drift scan: {Total} baseline entries, {Drifted} drifted", entries.Count, items.Count);
    }

    /// <summary>Sentinel used when a registry value is absent (matches the module delete conventions).</summary>
    public const string AbsentValue = "__absent__";

    private string? ReadCurrent(DriftBaselineEntry entry, string location)
    {
        var separator = location.LastIndexOf('\\');
        if (separator <= 0)
            return null;
        var keyPath = location[..separator];
        var valueName = location[(separator + 1)..];
        if (valueName == "(Default)")
            valueName = string.Empty;

        switch (entry.ValueType)
        {
            case ChangeValueType.Registry_DWord:
            {
                var read = _registry.ReadDWord(keyPath, valueName);
                return read.IsSuccess ? read.Value.ToString(System.Globalization.CultureInfo.InvariantCulture) : AbsentValue;
            }
            case ChangeValueType.Registry_String:
            case ChangeValueType.Registry_ExpandString:
            {
                var read = entry.ValueType == ChangeValueType.Registry_ExpandString
                    ? _registry.ReadExpandString(keyPath, valueName)
                    : _registry.ReadString(keyPath, valueName);
                return read.IsSuccess ? read.Value : AbsentValue;
            }
            case ChangeValueType.Registry_Binary:
            {
                var read = _registry.ReadBinary(keyPath, valueName);
                return read.IsSuccess ? Convert.ToHexString(read.Value ?? []) : AbsentValue;
            }
            case ChangeValueType.Registry_MultiString:
            {
                var read = _registry.ReadMultiString(keyPath, valueName);
                return read.IsSuccess ? string.Join('\0', read.Value ?? []) : AbsentValue;
            }
            default:
                return null; // untrackable type that leaked into the baseline
        }
    }

    /// <summary>Absent, empty, and delete-sentinel values all mean "no value here".</summary>
    private static string Normalize(string value) =>
        value.Length == 0 || value == AbsentValue ? string.Empty : value;

    private static string? SuspectCause(string systemLocation)
    {
        if (systemLocation.Contains(@"\Policies\", StringComparison.OrdinalIgnoreCase)
            || systemLocation.Contains("WindowsUpdate", StringComparison.OrdinalIgnoreCase))
            return "Windows Update or Group Policy refresh";
        if (systemLocation.Contains(@"\Explorer\", StringComparison.OrdinalIgnoreCase))
            return "Windows Update or Explorer settings refresh";
        if (systemLocation.Contains("WindowsCopilot", StringComparison.OrdinalIgnoreCase)
            || systemLocation.Contains("Windows Search", StringComparison.OrdinalIgnoreCase))
            return "Web Experience Pack or feature update";
        return null;
    }
}
