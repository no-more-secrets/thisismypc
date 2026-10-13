using Microsoft.Win32;
using NLog;
using WinRegistry = Microsoft.Win32.Registry;
using ThisIsMyPC.Core.Services;
using static ThisIsMyPC.Interop.Win32.Restore.NativeRestore;

namespace ThisIsMyPC.Interop.Win32.Restore;

/// <summary>
/// Creates System Restore points via SRSetRestorePointW. Windows skips creation when a
/// restore point already exists within SystemRestorePointCreationFrequency minutes
/// (default 1440), which would silently defeat the pre-debloat safety net; so the
/// frequency value is forced to 0 for the duration of the call and restored afterward.
/// Disabled protection is enabled on the Windows drive before one creation retry.
/// Transient operational registry state, same category as the 26-8 GPCache clears;
/// deliberately not staged through the pending-changes pipeline.
/// </summary>
public sealed class RestorePointService : IRestorePointService
{
    private const string SystemRestoreDisabledMessage =
        "System Restore is disabled";

    // Serializes creations so two concurrent FrequencyOverride scopes can never
    // interleave and clobber the real SystemRestorePointCreationFrequency value.
    private static readonly SemaphoreSlim CreationGate = new(1, 1);

    private readonly ILogger _logger;

    public RestorePointService(ILogger? logger = null)
    {
        _logger = logger ?? LogManager.GetLogger("ThisIsMyPC.Interop.Win32.Restore.RestorePointService");
    }

    public Task<RestorePointResult> CreateRestorePointAsync(string description)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(description);
        return Task.Run(() =>
        {
            CreationGate.Wait();
            try
            {
                return Create(description);
            }
            finally
            {
                CreationGate.Release();
            }
        });
    }

    private unsafe RestorePointResult Create(string description)
    {
        try
        {
            using var frequencyOverride = new FrequencyOverride(_logger);

            return CreateWithProtection(description, TryCreateNative, EnableSystemDriveProtection);
        }
        catch (DllNotFoundException)
        {
            return new RestorePointResult
            {
                Outcome = RestorePointOutcome.Failed,
                Message = "The System Restore API (srclient.dll) is not available on this system",
            };
        }
        catch (EntryPointNotFoundException)
        {
            return new RestorePointResult
            {
                Outcome = RestorePointOutcome.Failed,
                Message = "The System Restore API (srclient.dll) is not available on this system",
            };
        }
#pragma warning disable CA1031 // must never fault the apply command's Task; always return a Failed result
        catch (Exception ex)
        {
            _logger.Error(ex, "Restore point creation threw unexpectedly");
            return new RestorePointResult
            {
                Outcome = RestorePointOutcome.Failed,
                Message = $"Restore point creation failed: {ex.Message}",
            };
        }
#pragma warning restore CA1031
    }

    // Enable protection only after Windows reports it disabled, then retry once.
    // This coordinator is tested without calling Windows or changing the registry.
    internal static RestorePointResult CreateWithProtection(
        string description,
        Func<string, RestorePointResult> create,
        Func<uint> enableProtection)
    {
        var result = create(description);
        if (result.Outcome != RestorePointOutcome.SystemRestoreDisabled)
            return result;

        var status = enableProtection();
        if (status != ERROR_SUCCESS)
        {
            return new RestorePointResult
            {
                Outcome = RestorePointOutcome.SystemRestoreDisabled,
                Message = status == ERROR_ACCESS_DISABLED_BY_POLICY
                    ? "System Restore is disabled by administrator policy"
                    : $"Could not enable System Restore (Windows error {status})",
            };
        }

        return create(description);
    }

    private uint EnableSystemDriveProtection()
    {
        // Respect administrator policy. Never remove or replace its values.
        using var policy = WinRegistry.LocalMachine.OpenSubKey(
            @"SOFTWARE\Policies\Microsoft\Windows NT\SystemRestore");
        if (policy?.GetValue("DisableSR") is int disableRestore && disableRestore != 0
            || policy?.GetValue("DisableConfig") is int disableConfig && disableConfig != 0)
            return ERROR_ACCESS_DISABLED_BY_POLICY;

        var drive = Path.GetPathRoot(Environment.SystemDirectory);
        if (string.IsNullOrWhiteSpace(drive))
            return ERROR_INVALID_DRIVE;

        var status = EnableSR(drive);
        _logger.Info("System Restore enable request on {Drive}: Windows status {Status}", drive, status);
        return status;
    }

    private unsafe RestorePointResult TryCreateNative(string description)
    {
        var info = new RESTOREPOINTINFOW
        {
            dwEventType = BEGIN_SYSTEM_CHANGE,
            dwRestorePtType = MODIFY_SETTINGS,
            llSequenceNumber = 0,
        };
        CopyDescription(description, ref info);

        STATEMGRSTATUS status;
        var succeeded = SRSetRestorePointW(&info, &status);

        var result = ReadCreationResult(succeeded, status.nStatus, status.llSequenceNumber);
        if (!result.IsSuccess)
        {
            _logger.Warn("Restore point creation refused: {Message}", result.Message);
            return result;
        }

        EndSystemChange(status.llSequenceNumber);
        _logger.Info("Restore point {SequenceNumber} created: {Description}", status.llSequenceNumber, description);
        return result;
    }

    internal static RestorePointResult ReadCreationResult(bool succeeded, uint status, long sequenceNumber)
    {
        // A successful status without a sequence does not identify a restore point.
        if (status == ERROR_SERVICE_DISABLED)
        {
            return new RestorePointResult
            {
                Outcome = RestorePointOutcome.SystemRestoreDisabled,
                Message = SystemRestoreDisabledMessage,
            };
        }

        if (!succeeded || status != ERROR_SUCCESS || sequenceNumber <= 0)
        {
            return new RestorePointResult
            {
                Outcome = RestorePointOutcome.Failed,
                Message = succeeded && status == ERROR_SUCCESS
                    ? "Windows did not return a restore point sequence number"
                    : $"Restore point creation failed (Windows error {status})",
            };
        }

        return new RestorePointResult
        {
            Outcome = RestorePointOutcome.Created,
            SequenceNumber = sequenceNumber,
        };
    }

    // MSDN-recommended pairing: close the BEGIN_SYSTEM_CHANGE bracket so the point is
    // finalized immediately. Best-effort; the restore point already exists if this fails.
    private unsafe void EndSystemChange(long sequenceNumber)
    {
        var end = new RESTOREPOINTINFOW
        {
            dwEventType = END_SYSTEM_CHANGE,
            dwRestorePtType = MODIFY_SETTINGS,
            llSequenceNumber = sequenceNumber,
        };

        STATEMGRSTATUS status;
        if (!SRSetRestorePointW(&end, &status) || status.nStatus != ERROR_SUCCESS)
            _logger.Warn("END_SYSTEM_CHANGE for restore point {SequenceNumber} failed with status {Status}",
                sequenceNumber, status.nStatus);
    }

    private static unsafe void CopyDescription(string description, ref RESTOREPOINTINFOW info)
    {
        fixed (char* buffer = info.szDescription)
        {
            var span = new Span<char>(buffer, MaxDescriptionChars);
            var length = Math.Min(description.Length, span.Length - 1); // keep the null terminator
            description.AsSpan(0, length).CopyTo(span);
            span[length] = '\0';
        }
    }

    /// <summary>
    /// Zeroes SystemRestorePointCreationFrequency for the duration of one creation call,
    /// then restores the previous value (or removes it if it was absent). If the override
    /// cannot be applied the creation proceeds anyway; Windows may then reuse a recent
    /// restore point instead of creating a fresh one.
    /// </summary>
    private sealed class FrequencyOverride : IDisposable
    {
        private const string KeyPath = @"SOFTWARE\Microsoft\Windows NT\CurrentVersion\SystemRestore";
        private const string ValueName = "SystemRestorePointCreationFrequency";

        private readonly ILogger _logger;
        private readonly bool _applied;
        private readonly object? _originalValue;
        private readonly RegistryValueKind _originalKind = RegistryValueKind.DWord;

        public FrequencyOverride(ILogger logger)
        {
            _logger = logger;
            try
            {
                using var key = WinRegistry.LocalMachine.OpenSubKey(KeyPath, writable: true);
                if (key is null)
                    return;

                _originalValue = key.GetValue(ValueName);
                if (_originalValue is not null)
                    _originalKind = key.GetValueKind(ValueName);
                key.SetValue(ValueName, 0, RegistryValueKind.DWord);
                _applied = true;
            }
#pragma warning disable CA1031 // override is best-effort; creation must proceed regardless
            catch (Exception ex)
            {
                _logger.Warn(ex, "Could not override restore point creation frequency; a recent restore point may be reused");
            }
#pragma warning restore CA1031
        }

        public void Dispose()
        {
            if (!_applied)
                return;

            try
            {
                using var key = WinRegistry.LocalMachine.OpenSubKey(KeyPath, writable: true);
                if (key is null)
                    return;

                if (_originalValue is null)
                    key.DeleteValue(ValueName, throwOnMissingValue: false);
                else
                    key.SetValue(ValueName, _originalValue, _originalKind);
            }
#pragma warning disable CA1031 // Dispose must never throw into the apply command's Task
            catch (Exception ex)
            {
                _logger.Warn(ex, "Could not restore SystemRestorePointCreationFrequency after restore point creation");
            }
#pragma warning restore CA1031
        }
    }
}
