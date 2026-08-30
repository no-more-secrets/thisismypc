using System.Runtime.InteropServices;

namespace ThisIsMyPC.Interop.Win32.Security;

public sealed class SecurityApi : ISecurityApi
{
    private const ushort SeDaclProtected = 0x1000;

    public uint ApplyDacl(string directoryPath, DaclAccessEntry[] entries, bool disableInheritance)
    {
        var nativeEntries = new ExplicitAccessW[entries.Length];
        var pinnedStrings = new nint[entries.Length];

        try
        {
            for (int i = 0; i < entries.Length; i++)
            {
                pinnedStrings[i] = Marshal.StringToCoTaskMemUni(entries[i].TrusteeName);
                nativeEntries[i] = new ExplicitAccessW
                {
                    grfAccessPermissions = entries[i].AccessPermissions,
                    grfAccessMode = (uint)AccessMode.SetAccess,
                    grfInheritance = entries[i].Inheritance,
                    Trustee = new TrusteeW
                    {
                        pMultipleTrustee = nint.Zero,
                        MultipleTrusteeOperation = (uint)MultipleTrusteeOperation.NoMultipleTrustee,
                        TrusteeForm = (uint)TrusteeForm.TrusteeIsName,
                        TrusteeType = (uint)TrusteeType.TrusteeIsWellKnownGroup,
                        ptstrName = pinnedStrings[i],
                    },
                };
            }

            uint result = NativeSecurity.SetEntriesInAclW(
                (uint)nativeEntries.Length,
                nativeEntries,
                nint.Zero,
                out nint pNewDacl);

            if (result != 0)
                return result;

            if (pNewDacl == nint.Zero)
                return 87; // ERROR_INVALID_PARAMETER; SetEntriesInAcl returned success but null DACL

            try
            {
                uint secInfo = (uint)SecurityInformationFlags.DaclSecurityInformation;
                if (disableInheritance)
                    secInfo |= (uint)SecurityInformationFlags.ProtectedDaclSecurityInformation;

                return NativeSecurity.SetNamedSecurityInfoW(
                    directoryPath,
                    (uint)SeObjectType.SeFileObject,
                    secInfo,
                    nint.Zero,
                    nint.Zero,
                    pNewDacl,
                    nint.Zero);
            }
            finally
            {
                NativeSecurity.LocalFree(pNewDacl);
            }
        }
        finally
        {
            foreach (nint ptr in pinnedStrings)
            {
                if (ptr != nint.Zero)
                    Marshal.FreeCoTaskMem(ptr);
            }
        }
    }

    public DaclInfo ReadDacl(string directoryPath)
    {
        uint result = NativeSecurity.GetNamedSecurityInfoW(
            directoryPath,
            (uint)SeObjectType.SeFileObject,
            (uint)SecurityInformationFlags.DaclSecurityInformation,
            out _,
            out _,
            out nint pDacl,
            out _,
            out nint pSecDesc);

        if (result != 0)
            return DaclInfo.Failure($"GetNamedSecurityInfoW failed with error {result}");

        try
        {
            bool isProtected = CheckDaclProtected(pSecDesc);
            var aceEntries = ReadAceEntries(pDacl);
            return new DaclInfo(isProtected, aceEntries);
        }
        finally
        {
            NativeSecurity.LocalFree(pSecDesc);
        }
    }

    private static bool CheckDaclProtected(nint pSecDesc)
    {
        if (!NativeSecurity.GetSecurityDescriptorControl(pSecDesc, out ushort control, out _))
            return false;

        return (control & SeDaclProtected) != 0;
    }

    private static List<AceInfo> ReadAceEntries(nint pDacl)
    {
        var entries = new List<AceInfo>();

        if (pDacl == nint.Zero)
            return entries;

        if (!NativeSecurity.GetAclInformation(
                pDacl,
                out AclSizeInformation aclInfo,
                (uint)Marshal.SizeOf<AclSizeInformation>(),
                (uint)AclInformationClass.AclSizeInformation))
        {
            return entries;
        }

        for (uint i = 0; i < aclInfo.AceCount; i++)
        {
            if (!NativeSecurity.GetAce(pDacl, i, out nint pAce))
                continue;

            // Read all ACE types (allowed, denied, audit); they share the same
            // header+mask+sid layout. Including deny/audit ACEs ensures the guard's
            // count check catches injected deny ACEs that would block access.
            uint accessMask = (uint)Marshal.ReadInt32(pAce + Marshal.SizeOf<AceHeader>());
            nint sidPtr = pAce + Marshal.SizeOf<AceHeader>() + sizeof(uint);

            string? sid = ConvertSidToString(sidPtr);
            if (sid is not null)
                entries.Add(new AceInfo(sid, accessMask));
        }

        return entries;
    }

    private static string? ConvertSidToString(nint sidPtr)
    {
        if (!NativeSecurity.ConvertSidToStringSidW(sidPtr, out nint stringSidPtr))
            return null;

        try
        {
            return Marshal.PtrToStringUni(stringSidPtr);
        }
        finally
        {
            NativeSecurity.LocalFree(stringSidPtr);
        }
    }
}
