using Microsoft.Win32;
using Serilog;
using WinRegistry = Microsoft.Win32.Registry;
using ThisIsMyPC.Core.Services;
using static ThisIsMyPC.Interop.Win32.Restore.NativeRestore;

namespace ThisIsMyPC.Interop.Win32.Restore;

/// <summary>
/// Creates System Restore points via SRSetRestorePointW. Windows skips creation when a
/// restore point already exists within SystemRestorePointCreationFrequency minutes
/// (default 1440), which would silently defeat the pre-debloat safety net — so the
/// frequency value is forced to 0 for the duration of the call and restored afterward.
/// Transient operational registry state, same category as the 26-8 GPCache clears;
/// deliberately not staged through the pending-changes pipeline.
/// </summary>
public sealed class RestorePointService : IRestorePointService
{
    private const string SystemRestoreDisabledMessage =
        "System Restore is disabled — enable it in System Properties > System Protection";

    // Serializes creations so two concurrent FrequencyOverride scopes can never
    // interleave and clobber the real SystemRestorePointCreationFrequency value.
    private static readonly SemaphoreSlim CreationGate = new(1, 1);

    private readonly ILogger _logger;

    public RestorePointService(ILogger? logger = null)
    {
        _logger = logger ?? Log.Logger;
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

            var info = new RESTOREPOINTINFOW
            {
                dwEventType = BEGIN_SYSTEM_CHANGE,
                dwRestorePtType = MODIFY_SETTINGS,
                llSequenceNumber = 0,
            };
            CopyDescription(description, ref info);

            STATEMGRSTATUS status;
            var succeeded = SRSetRestorePointW(&info, &status);

            if (!succeeded || status.nStatus != ERROR_SUCCESS)
            {
                if (status.nStatus == ERROR_SERVICE_DISABLED)
                {
                    _logger.Warning("Restore point creation refused: System Restore is disabled");
                    return new RestorePointResult
                    {
                        Outcome = RestorePointOutcome.SystemRestoreDisabled,
                        Message = SystemRestoreDisabledMessage,
                    };
                }

                _logger.Warning("SRSetRestorePoint failed with status {Status}", status.nStatus);
                return new RestorePointResult
                {
                    Outcome = RestorePointOutcome.Failed,
                    Message = $"Restore point creation failed (Windows error {status.nStatus})",
                };
            }

            var sequenceNumber = status.llSequenceNumber;
            EndSystemChange(sequenceNumber);

            _logger.Information(
                "Restore point {SequenceNumber} created: {Description}", sequenceNumber, description);
            return new RestorePointResult
            {
                Outcome = RestorePointOutcome.Created,
                SequenceNumber = sequenceNumber,
            };
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
#pragma warning disable CA1031 // must never fault the apply command's Task — always return a Failed result
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

    // MSDN-recommended pairing: close the BEGIN_SYSTEM_CHANGE bracket so the point is
    // finalized immediately. Best-effort — the restore point already exists if this fails.
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
            _logger.Warning("END_SYSTEM_CHANGE for restore point {SequenceNumber} failed with status {Status}",
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
    /// cannot be applied the creation proceeds anyway — Windows may then reuse a recent
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
#pragma warning disable CA1031 // override is best-effort — creation must proceed regardless
            catch (Exception ex)
            {
                _logger.Warning(ex, "Could not override restore point creation frequency; a recent restore point may be reused");
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
                _logger.Warning(ex, "Could not restore SystemRestorePointCreationFrequency after restore point creation");
            }
#pragma warning restore CA1031
        }
    }
}
