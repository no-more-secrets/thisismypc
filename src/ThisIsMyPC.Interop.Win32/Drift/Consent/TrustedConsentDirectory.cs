using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;
using ThisIsMyPC.Interop.Win32.Coordination;
using ThisIsMyPC.Interop.Win32.Security;

namespace ThisIsMyPC.Interop.Win32.Drift.Consent;

/// <summary>Holds every directory component against write/delete sharing until file access finishes.</summary>
internal sealed class TrustedConsentDirectory : IDisposable
{
    private readonly List<SafeFileHandle> _ancestors = [];
    public string Path { get; }

    public TrustedConsentDirectory(string directory)
    {
        Path = System.IO.Path.GetFullPath(directory).TrimEnd('\\');
        if (Path.Length < 3 || Path[1] != ':' || Path[2] != '\\' || Path.AsSpan(2).Contains(':'))
            throw new ConsentTrustException("Consent requires a local absolute directory without alternate streams.");
        try
        {
            var component = Path[..3];
            if (NativeConsentFiles.GetDriveTypeW(component) != 3)
                throw new ConsentTrustException("Consent storage requires a fixed local drive.");
            Hold(component);
            foreach (var part in Path[3..].Split('\\', StringSplitOptions.RemoveEmptyEntries))
            {
                if (part.EndsWith(' ') || part.EndsWith('.'))
                    throw new ConsentTrustException("Consent directory components must not use trailing-space or trailing-dot aliases.");
                component = System.IO.Path.Combine(component, part);
                Hold(component);
            }
            VerifySecurity(_ancestors[^1], directory: true);
        }
        catch
        {
            Dispose();
            throw;
        }
    }

    private void Hold(string path)
    {
        // No write or delete sharing: existing incompatible handles also cause refusal.
        var handle = NativeConsentFiles.CreateFileW(path,
            NativeConsentFiles.ReadControl | NativeConsentFiles.ReadAttributes, 1, 0, 3,
            NativeConsentFiles.OpenReparsePoint | NativeConsentFiles.BackupSemantics, 0);
        if (handle.IsInvalid)
        {
            handle.Dispose();
            throw new IOException($"Cannot hold consent directory ancestry (Win32 {Marshal.GetLastPInvokeError()}).");
        }
        _ancestors.Add(handle);
        VerifyShape(handle, directory: true);
    }

    internal static NativeConsentFiles.FileInformation VerifyShape(SafeFileHandle handle, bool directory)
    {
        if (!NativeConsentFiles.GetFileInformationByHandle(handle, out var info))
            throw new IOException($"Cannot inspect consent handle (Win32 {Marshal.GetLastPInvokeError()}).");
        if ((info.Attributes & NativeConsentFiles.ReparsePoint) != 0)
            throw new ConsentTrustException("Consent ancestry and files must not be reparse points.");
        if (((info.Attributes & NativeConsentFiles.Directory) != 0) != directory)
            throw new ConsentTrustException("Consent path has the wrong object type.");
        if (!directory && info.NumberOfLinks != 1)
            throw new ConsentTrustException("Consent files must have exactly one link.");
        return info;
    }

    internal static void VerifySecurity(SafeFileHandle handle, bool directory)
    {
        var result = NativeMutationLease.GetSecurityInfo(handle.DangerousGetHandle(), 1, 5,
            out var owner, out _, out var dacl, out _, out var descriptor);
        if (result != 0)
            throw new ConsentTrustException($"Cannot read consent security (Win32 {result}).");
        try
        {
            if (Sid(owner) is not ("S-1-5-18" or "S-1-5-32-544"))
                throw new ConsentTrustException("Consent owner must be SYSTEM or Administrators.");
            if (dacl == 0 || !NativeSecurity.GetSecurityDescriptorControl(descriptor, out var control, out _) || (control & 0x1000) == 0)
                throw new ConsentTrustException("Consent requires a protected, non-null DACL.");
            if (!NativeSecurity.GetAclInformation(dacl, out var info, (uint)Marshal.SizeOf<AclSizeInformation>(), 2) || info.AceCount != 2)
                throw new ConsentTrustException("Consent requires exactly two allow entries.");
            var sids = new HashSet<string>(StringComparer.Ordinal);
            for (uint i = 0; i < info.AceCount; i++)
            {
                if (!NativeSecurity.GetAce(dacl, i, out var ace))
                    throw new ConsentTrustException("Cannot read a consent access entry.");
                var header = Marshal.PtrToStructure<AceHeader>(ace);
                if (header.AceType != 0 || header.AceSize < 20 || header.AceFlags != (directory ? 3 : 0))
                    throw new ConsentTrustException("Consent access entry type or inheritance flags are unsupported.");
                if (unchecked((uint)Marshal.ReadInt32(ace, 4)) != 0x1F01FF)
                    throw new ConsentTrustException("Consent access entries must grant full file access only.");
                if (header.AceSize != 16 + 4 * Marshal.ReadByte(ace, 9))
                    throw new ConsentTrustException("Consent access entry SID length is invalid.");
                var sid = Sid(ace + 8);
                if (sid is not ("S-1-5-18" or "S-1-5-32-544") || !sids.Add(sid))
                    throw new ConsentTrustException("Consent grants access to an unexpected or duplicate identity.");
            }
        }
        finally { NativeSecurity.LocalFree(descriptor); }
    }

    private static string? Sid(nint sid)
    {
        if (sid == 0 || !NativeSecurity.ConvertSidToStringSidW(sid, out var text))
            return null;
        try { return Marshal.PtrToStringUni(text); }
        finally { NativeSecurity.LocalFree(text); }
    }

    public void Dispose()
    {
        for (var i = _ancestors.Count - 1; i >= 0; i--)
            _ancestors[i].Dispose();
        _ancestors.Clear();
    }
}

internal sealed class ConsentTrustException(string message) : IOException(message);