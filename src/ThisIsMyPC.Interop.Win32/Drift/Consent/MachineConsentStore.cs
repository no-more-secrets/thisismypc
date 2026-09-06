using System.Runtime.InteropServices;
using ThisIsMyPC.Core;
using ThisIsMyPC.Core.Coordination;
using ThisIsMyPC.Core.Drift.Consent;
using ThisIsMyPC.Interop.Win32.Coordination;
using ThisIsMyPC.Interop.Win32.Security;

namespace ThisIsMyPC.Interop.Win32.Drift.Consent;

/// <summary>
/// Consent in an existing trusted local directory. No directory creation, ACL repair, or production wiring.
/// Ancestors remain held while file handles are checked and used. Unsafe access returns consent off.
/// </summary>
public sealed class MachineConsentStore : IMachineConsentStore
{
    public const string FileName = "owner-mode-consent.json";
    private readonly string _directory;
    private readonly string _leaseName;
    private readonly TimeProvider _time;

    public MachineConsentStore(string? directory = null, string? leaseName = null, TimeProvider? timeProvider = null)
    {
        _directory = directory ?? AppConstants.DataDirectoryPath;
        _leaseName = leaseName ?? MutationLeaseNames.Production;
        _time = timeProvider ?? TimeProvider.System;
    }

    public MachineConsentState Read()
    {
        try
        {
            using var scope = new TrustedConsentDirectory(_directory);
            return ReadUnderScope(scope);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        {
            return Failure(ex);
        }
    }

    private MachineConsentState ReadUnderScope(TrustedConsentDirectory scope)
    {
        using var handle = NativeConsentFiles.CreateFileW(System.IO.Path.Combine(scope.Path, FileName),
            NativeConsentFiles.GenericRead | NativeConsentFiles.ReadControl, 1, 0, 3,
            NativeConsentFiles.OpenReparsePoint, 0);
        if (handle.IsInvalid)
        {
            var error = Marshal.GetLastPInvokeError();
            return MachineConsentState.Off(error == 2 ? MachineConsentStatus.Missing : MachineConsentStatus.Unavailable,
                $"Consent could not be opened (Win32 {error}).");
        }
        var info = TrustedConsentDirectory.VerifyShape(handle, directory: false);
        TrustedConsentDirectory.VerifySecurity(handle, directory: false);
        if (info.FileSizeHigh != 0 || info.FileSizeLow > MachineConsentDocument.MaximumBytes)
            return MachineConsentState.Off(MachineConsentStatus.Corrupt, "Consent document exceeds its size limit.");
        using var stream = new FileStream(handle, FileAccess.Read);
        var bytes = new byte[checked((int)info.FileSizeLow)];
        stream.ReadExactly(bytes);
        return MachineConsentDocument.Parse(bytes, _time.GetUtcNow());
    }

    public MachineConsentWriteResult SetEnabled(bool enabled, IMutationLease lease)
    {
        ArgumentNullException.ThrowIfNull(lease);
        if (!CanSet(enabled, lease))
            return new(false, MachineConsentState.Off(MachineConsentStatus.LeaseRequired, "A matching held lease is required; enabling also requires recovery."));
        try
        {
            using var scope = new TrustedConsentDirectory(_directory);
            var previous = ReadUnderScope(scope);
            if (previous.Status is MachineConsentStatus.Untrusted or MachineConsentStatus.Unavailable)
                return new(false, previous);
            // Existing malformed content may be replaced by explicit consent, but only after file trust passed.
            var bytes = MachineConsentDocument.Encode(enabled, _time.GetUtcNow());
            var temp = System.IO.Path.Combine(scope.Path, $".{FileName}.{Guid.NewGuid():N}.tmp");
            try
            {
                WriteNewProtected(temp, bytes);
                if (!CanSet(enabled, lease))
                    return new(false, MachineConsentState.Off(MachineConsentStatus.LeaseRequired, "Mutation lease ended before consent replacement."));
                // Same-directory atomic rename. WRITE_THROUGH waits for the move to reach storage.
                if (!NativeConsentFiles.MoveFileExW(temp, System.IO.Path.Combine(scope.Path, FileName), 0x1 | 0x8))
                    throw new IOException($"Consent replacement failed (Win32 {Marshal.GetLastPInvokeError()}).");
                var loaded = ReadUnderScope(scope);
                if (loaded.Status != MachineConsentStatus.Loaded || loaded.Enabled != enabled)
                    return new(false, MachineConsentState.Off(MachineConsentStatus.Unavailable, "Consent replacement could not be confirmed."));
                return new(true, loaded);
            }
            finally
            {
                // Unique file created only in the held trusted directory. Never delete the destination on failure.
                if (File.Exists(temp))
                    File.Delete(temp);
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        {
            return new(false, Failure(ex));
        }
    }

    // Disabling must remain possible when journal recovery fails. It grants no restoration writes.
    private bool CanSet(bool enabled, IMutationLease lease) => lease.IsHeld
        && (!enabled || lease.CanWrite)
        && string.Equals(lease.Name, _leaseName, StringComparison.Ordinal);

    private static void WriteNewProtected(string path, byte[] bytes)
    {
        if (!NativeMutationLease.ConvertStringSecurityDescriptorToSecurityDescriptorW(
                "O:BAG:BAD:P(A;;FA;;;SY)(A;;FA;;;BA)", 1, out var descriptor, out _))
            throw new IOException($"Cannot construct consent security (Win32 {Marshal.GetLastPInvokeError()}).");
        var attributes = new NativeMutationLease.SecurityAttributes
        {
            Length = (uint)Marshal.SizeOf<NativeMutationLease.SecurityAttributes>(), SecurityDescriptor = descriptor,
        };
        var pointer = Marshal.AllocHGlobal(Marshal.SizeOf<NativeMutationLease.SecurityAttributes>());
        try
        {
            Marshal.StructureToPtr(attributes, pointer, false);
            using var handle = NativeConsentFiles.CreateFileW(path,
                NativeConsentFiles.GenericRead | NativeConsentFiles.GenericWrite | NativeConsentFiles.ReadControl,
                0, pointer, 1, NativeConsentFiles.OpenReparsePoint, 0);
            if (handle.IsInvalid)
                throw new IOException($"Cannot create protected consent file (Win32 {Marshal.GetLastPInvokeError()}).");
            TrustedConsentDirectory.VerifyShape(handle, directory: false);
            TrustedConsentDirectory.VerifySecurity(handle, directory: false);
            using var stream = new FileStream(handle, FileAccess.ReadWrite);
            stream.Write(bytes);
            stream.Flush(flushToDisk: true);
        }
        finally
        {
            Marshal.FreeHGlobal(pointer);
            NativeSecurity.LocalFree(descriptor);
        }
    }

    private static MachineConsentState Failure(Exception ex) => MachineConsentState.Off(
        ex is ConsentTrustException ? MachineConsentStatus.Untrusted : MachineConsentStatus.Unavailable, ex.Message);
}