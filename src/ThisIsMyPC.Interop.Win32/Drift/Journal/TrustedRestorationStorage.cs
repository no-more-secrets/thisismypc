using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;
using ThisIsMyPC.Interop.Win32.Coordination;
using ThisIsMyPC.Interop.Win32.Drift.Consent;
using ThisIsMyPC.Interop.Win32.Security;

namespace ThisIsMyPC.Interop.Win32.Drift.Journal;

/// <summary>
/// Holds existing trusted storage against replacement. Administrators and SYSTEM are trusted writers.
/// The caller keeps this scope alive through all journal and SQLite connections, disables SQLite pooling,
/// and sets journal_mode=PERSIST before transactions. All other database writers must use the same mode.
/// No directory, file, ownership, or ACL is repaired. Existing WAL/SHM files require offline reconciliation.
/// </summary>
public sealed class TrustedRestorationStorage : IDisposable
{
    private readonly TrustedConsentDirectory _data;
    private readonly TrustedConsentDirectory _journal;
    private readonly Dictionary<string, SafeFileHandle> _files = new(StringComparer.OrdinalIgnoreCase);
    private readonly Lock _gate = new();
    private bool _disposed;

    public string HistoryPath { get; }
    public string JournalPath => _journal.Path;

    public TrustedRestorationStorage(string dataDirectory, string journalDirectory)
    {
        _data = new TrustedConsentDirectory(dataDirectory);
        try
        {
            _journal = new TrustedConsentDirectory(journalDirectory);
            HistoryPath = Path.Combine(_data.Path, "history.db");
            VerifyHistoryFiles();
            foreach (var path in Directory.EnumerateFileSystemEntries(_journal.Path))
                if (!CheckJournalPath(path)) throw new IOException("Unexpected journal path.");
        }
        catch
        {
            foreach (var handle in _files.Values) handle.Dispose();
            _journal?.Dispose();
            _data.Dispose();
            throw;
        }
    }

    /// <summary>Valid only while this scope remains alive. Throws on unsafe existing objects.</summary>
    public bool CheckJournalPath(string path)
    {
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            var full = Path.GetFullPath(path);
            if (string.Equals(full.TrimEnd('\\'), _journal.Path, StringComparison.OrdinalIgnoreCase)) return true;
            if (!string.Equals(Path.GetDirectoryName(full), _journal.Path, StringComparison.OrdinalIgnoreCase)
                || Path.GetExtension(full) != ".tipj"
                || !Guid.TryParseExact(Path.GetFileNameWithoutExtension(full), "N", out _)) return false;
            return Pin(full, allowMissing: false);
        }
    }

    /// <summary>
    /// Call before each SQLite connection. Existing WAL/SHM files require offline reconciliation.
    /// A connection may create its own sidecars inside the held trusted parent.
    /// </summary>
    public void VerifyHistoryFiles()
    {
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            foreach (var suffix in new[] { "-wal", "-shm" })
            {
                if (Pin(HistoryPath + suffix, allowMissing: true))
                    throw new IOException("Existing SQLite sidecars require reconciliation before Owner Mode starts.");
            }
            Pin(HistoryPath + "-journal", allowMissing: true);
            Pin(HistoryPath, allowMissing: true);
        }
    }

    private bool Pin(string path, bool allowMissing)
    {
        if (_files.TryGetValue(path, out var prior))
        {
            TrustedConsentDirectory.VerifyShape(prior, directory: false);
            VerifyChildSecurity(prior);
            return true;
        }
        var handle = NativeConsentFiles.CreateFileW(path,
            NativeConsentFiles.GenericRead | NativeConsentFiles.ReadControl | NativeConsentFiles.ReadAttributes, 3, 0, 3,
            NativeConsentFiles.OpenReparsePoint, 0);
        if (handle.IsInvalid)
        {
            var error = Marshal.GetLastPInvokeError();
            handle.Dispose();
            if (allowMissing && error == 2) return false;
            throw new IOException($"Cannot hold restoration file (Win32 {error}).");
        }
        try
        {
            TrustedConsentDirectory.VerifyShape(handle, directory: false);
            VerifyChildSecurity(handle);
            _files.Add(path, handle);
            return true;
        }
        catch { handle.Dispose(); throw; }
    }

    private static void VerifyChildSecurity(SafeFileHandle handle)
    {
        var error = NativeMutationLease.GetSecurityInfo(handle.DangerousGetHandle(), 1, 5,
            out var owner, out _, out var dacl, out _, out var descriptor);
        if (error != 0) throw new IOException($"Cannot inspect restoration security (Win32 {error}).");
        try
        {
            if (Sid(owner) is not ("S-1-5-18" or "S-1-5-32-544") || dacl == 0
                || !NativeSecurity.GetSecurityDescriptorControl(descriptor, out var control, out _)
                || !NativeSecurity.GetAclInformation(dacl, out var info, (uint)Marshal.SizeOf<AclSizeInformation>(), 2)
                || info.AceCount != 2) throw new IOException("Restoration file ownership or DACL is unsafe.");
            var protectedAcl = (control & 0x1000) != 0;
            var identities = new HashSet<string>(StringComparer.Ordinal);
            for (uint i = 0; i < info.AceCount; i++)
            {
                if (!NativeSecurity.GetAce(dacl, i, out var ace)) throw new IOException("Cannot inspect restoration ACE.");
                var header = Marshal.PtrToStructure<AceHeader>(ace);
                if (header.AceType != 0 || header.AceFlags != (protectedAcl ? 0 : 0x10)
                    || header.AceSize < 20 || unchecked((uint)Marshal.ReadInt32(ace, 4)) != 0x1F01FF
                    || header.AceSize != 16 + 4 * Marshal.ReadByte(ace, 9))
                    throw new IOException("Restoration file access entries are unsafe.");
                var sid = Sid(ace + 8);
                if (sid is not ("S-1-5-18" or "S-1-5-32-544") || !identities.Add(sid))
                    throw new IOException("Restoration file grants unexpected access.");
            }
        }
        finally { NativeSecurity.LocalFree(descriptor); }
    }

    private static string? Sid(nint sid)
    {
        if (sid == 0 || !NativeSecurity.ConvertSidToStringSidW(sid, out var text)) return null;
        try { return Marshal.PtrToStringUni(text); }
        finally { NativeSecurity.LocalFree(text); }
    }

    public void Dispose()
    {
        lock (_gate)
        {
            if (_disposed) return;
            _disposed = true;
            foreach (var handle in _files.Values) handle.Dispose();
            _files.Clear();
            _journal.Dispose();
            _data.Dispose();
        }
    }
}
