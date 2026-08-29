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
/// Post-boot only — continuous event-driven detection is Phase 3 by design.
/// </summary>
public sealed class DriftWatchdog : IHostedService, IDriftReportSource
{
    private readonly IRegistryService _registry;
    private readonly ILogger<DriftWatchdog> _logger;
    private readonly string _baselinePath;
    private readonly Lock _sync = new();

    private DriftReportResponse _report = new() { BaselinePresent = false };
    private DateTimeOffset? _lastScanUtc;

    public DriftWatchdog(IRegistryService registry, ILogger<DriftWatchdog> logger, string? baselinePath = null)
    {
        _registry = registry;
        _logger = logger;
        _baselinePath = baselinePath
            ?? Path.Combine(AppConstants.MachineDataDirectoryPath, DriftBaselineStore.FileName);
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

        var items = new List<DriftItem>();
        foreach (var entry in entries)
        {
            var current = ReadCurrent(entry);
            if (current is null)
                continue; // unreadable — never report drift on a failed probe
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

    private string? ReadCurrent(DriftBaselineEntry entry)
    {
        var separator = entry.SystemLocation.LastIndexOf('\\');
        if (separator <= 0)
            return null;
        var keyPath = entry.SystemLocation[..separator];
        var valueName = entry.SystemLocation[(separator + 1)..];
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
