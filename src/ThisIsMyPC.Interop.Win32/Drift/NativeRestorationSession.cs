using System.Runtime.InteropServices;
using ThisIsMyPC.Core;
using ThisIsMyPC.Core.Coordination;
using ThisIsMyPC.Core.Data;
using ThisIsMyPC.Core.Drift;
using ThisIsMyPC.Core.Drift.Baseline;
using ThisIsMyPC.Core.Drift.Journal;
using ThisIsMyPC.Core.Results;
using ThisIsMyPC.Core.Services;
using ThisIsMyPC.Interop.Win32.Coordination;
using ThisIsMyPC.Interop.Win32.Drift.Baseline;
using ThisIsMyPC.Interop.Win32.Drift.Consent;
using ThisIsMyPC.Interop.Win32.Drift.Journal;
using ThisIsMyPC.Interop.Win32.Security;

namespace ThisIsMyPC.Interop.Win32.Drift;

/// <summary>Trusted native storage held through broker or service operations. Dispose after operations finish.</summary>
public sealed partial class NativeRestorationSession : IDisposable
{
    private readonly TrustedRestorationStorage _storage;
    private readonly MachineBaselineStorage _baselineStorage;
    private readonly IRegistryService _registry;
    private SingleOwnerBaselineStore? _baseline;
    private bool _initialized;
    public SingleOwnerBaselineStore Baseline => _baseline ?? throw new InvalidOperationException("No saved owner is loaded.");
    public MutationCoordinator Coordinator { get; }
    public NamedMutexMutationLeaseProvider Leases { get; }
    public MachineConsentStore Consent { get; }
    public RestorationJournal Journal { get; }
    public ChangeHistoryRepository History { get; } = new();

    public NativeRestorationSession(IRegistryService registry, string? primaryUserSid = null,
        string? dataDirectory = null, string? leaseName = null, TimeProvider? time = null)
    {
        _registry = registry;
        dataDirectory ??= AppConstants.DataDirectoryPath;
        leaseName ??= MutationLeaseNames.Production;
        time ??= TimeProvider.System;
        var journalPath = Path.Combine(dataDirectory, "restoration-journal");
        // The trusted machine parent must already exist. Only a new protected child is provisioned.
        using (var parent = new TrustedConsentDirectory(dataDirectory)) CreateJournalDirectory(journalPath);
        _storage = new(dataDirectory, journalPath);
        _baselineStorage = new(dataDirectory);
        if (primaryUserSid is not null) _baseline = new(_baselineStorage, leaseName, primaryUserSid);
        Leases = new(leaseName, "owner-mode");
        Consent = new(dataDirectory, leaseName, time);
        Journal = new(journalPath, new VerifiedGuard(_storage), _storage.CheckJournalPath, timeProvider: time);
        Coordinator = new(Leases, RecoverAsync);
    }

    private async Task<OperationResult<bool>> RecoverAsync(IMutationLease lease, CancellationToken token)
    {
        try
        {
            token.ThrowIfCancellationRequested();
            _baseline ??= SingleOwnerBaselineStore.OpenExisting(_baselineStorage, Leases.Name, lease);
            if (!_initialized)
            {
                await History.InitializeDatabaseAsync(_storage.HistoryPath, _storage.VerifyHistoryFiles).ConfigureAwait(false);
                _initialized = true;
            }
            return await new RestorationRecovery(Baseline, Journal, Consent, _registry, Leases.Name)
                .RecoverAsync(lease, token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception ex)
        {
            var off = Consent.SetEnabled(false, lease);
            return OperationResult<bool>.Failure("Owner Mode storage recovery failed: " + ex.Message +
                (off.IsSuccess ? string.Empty : ". Consent-off could not be confirmed."), ErrorCategory.ServiceUnavailable);
        }
    }

    private sealed class VerifiedGuard(TrustedRestorationStorage storage) : IDataDirectoryGuard
    {
        public OperationResult<DaclStatus> EnsureHardened(string path) => storage.CheckJournalPath(path)
            ? OperationResult<DaclStatus>.Success(DaclStatus.Verified)
            : OperationResult<DaclStatus>.Failure("Unexpected journal directory.", ErrorCategory.AccessDenied);
    }

    private static void CreateJournalDirectory(string path)
    {
        if (!NativeMutationLease.ConvertStringSecurityDescriptorToSecurityDescriptorW(
            "O:BAG:BAD:P(A;OICI;FA;;;SY)(A;OICI;FA;;;BA)", 1, out var descriptor, out _))
            throw new IOException("Cannot construct journal directory security.");
        var attributes = new NativeMutationLease.SecurityAttributes
        { Length = (uint)Marshal.SizeOf<NativeMutationLease.SecurityAttributes>(), SecurityDescriptor = descriptor };
        var pointer = Marshal.AllocHGlobal(Marshal.SizeOf<NativeMutationLease.SecurityAttributes>());
        try
        {
            Marshal.StructureToPtr(attributes, pointer, false);
            if (!CreateDirectoryW(path, pointer) && Marshal.GetLastPInvokeError() != 183)
                throw new IOException("Cannot create protected journal directory.");
        }
        finally { Marshal.FreeHGlobal(pointer); NativeSecurity.LocalFree(descriptor); }
    }

    [LibraryImport("kernel32.dll", SetLastError = true, StringMarshalling = StringMarshalling.Utf16)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool CreateDirectoryW(string path, nint attributes);

    public void Dispose() => _storage.Dispose();
}
