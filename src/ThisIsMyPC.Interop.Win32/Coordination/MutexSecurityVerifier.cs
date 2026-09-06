using System.Runtime.InteropServices;
using ThisIsMyPC.Interop.Win32.Security;

namespace ThisIsMyPC.Interop.Win32.Coordination;

/// <summary>
/// Reads the owner and DACL of an open mutex handle and decides whether the
/// object is one of ours: owned by SYSTEM or Administrators, protected by
/// exactly two allow ACEs (SYSTEM full, Administrators full) and nothing else.
/// A pre-existing object that fails this test is a squatter or a weakened lock;
/// the caller refuses it and never falls back to a looser descriptor.
/// </summary>
internal static class MutexSecurityVerifier
{
    private const string SystemSid = "S-1-5-18";
    private const string AdministratorsSid = "S-1-5-32-544";

    /// <summary>Null when the object is trusted; otherwise the reason it is not.</summary>
    internal static string? FindTrustProblem(nint mutexHandle)
    {
        uint status = NativeMutationLease.GetSecurityInfo(
            mutexHandle,
            NativeMutationLease.SeKernelObject,
            NativeMutationLease.OwnerSecurityInformation | NativeMutationLease.DaclSecurityInformation,
            out nint ownerSid,
            out _,
            out nint dacl,
            out _,
            out nint securityDescriptor);

        if (status != 0)
            return $"security of the lock object could not be read (win32={status})";

        try
        {
            string? owner = ConvertSidToString(ownerSid);
            if (owner is not (SystemSid or AdministratorsSid))
                return $"lock object owner is '{owner ?? "unreadable"}', not SYSTEM or Administrators";

            if (dacl == nint.Zero)
                return "lock object has a null DACL (everyone has access)";

            if (!NativeSecurity.GetAclInformation(
                    dacl,
                    out AclSizeInformation info,
                    (uint)Marshal.SizeOf<AclSizeInformation>(),
                    (uint)AclInformationClass.AclSizeInformation))
            {
                return "lock object DACL size could not be read";
            }

            if (info.AceCount != 2)
                return $"lock object DACL has {info.AceCount} entries, expected exactly 2";

            bool sawSystem = false;
            bool sawAdministrators = false;
            for (uint i = 0; i < info.AceCount; i++)
            {
                if (!NativeSecurity.GetAce(dacl, i, out nint ace))
                    return $"lock object DACL entry {i} could not be read";

                var header = Marshal.PtrToStructure<AceHeader>(ace);
                if (header.AceType != NativeMutationLease.AccessAllowedAceType)
                    return $"lock object DACL entry {i} is not an allow entry (type {header.AceType})";

                int headerSize = Marshal.SizeOf<AceHeader>();
                uint mask = (uint)Marshal.ReadInt32(ace + headerSize);
                if (mask is not (NativeMutationLease.MutexAllAccess or NativeMutationLease.GenericAll))
                    return $"lock object DACL entry {i} grants 0x{mask:X} instead of full access";

                string? sid = ConvertSidToString(ace + headerSize + sizeof(uint));
                switch (sid)
                {
                    case SystemSid when !sawSystem:
                        sawSystem = true;
                        break;
                    case AdministratorsSid when !sawAdministrators:
                        sawAdministrators = true;
                        break;
                    default:
                        return $"lock object DACL entry {i} names '{sid ?? "unreadable"}'";
                }
            }

            return sawSystem && sawAdministrators
                ? null
                : "lock object DACL does not name both SYSTEM and Administrators";
        }
        finally
        {
            NativeSecurity.LocalFree(securityDescriptor);
        }
    }

    private static string? ConvertSidToString(nint sid)
    {
        if (sid == nint.Zero || !NativeSecurity.ConvertSidToStringSidW(sid, out nint text))
            return null;

        try
        {
            return Marshal.PtrToStringUni(text);
        }
        finally
        {
            NativeSecurity.LocalFree(text);
        }
    }
}
