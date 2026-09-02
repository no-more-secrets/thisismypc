using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using ThisIsMyPC.Core.Services;

namespace ThisIsMyPC.Interop.Win32.Security;

/// <summary>
/// Who signed a file, the way Autoruns reports it. Embedded signatures go
/// through WinVerifyTrust; files without one (most of Windows itself) are
/// looked up by hash in the security catalogs and verified through the
/// catalog, whose signer ("Microsoft Windows") is then the file's signer.
/// Revocation uses the cache only, so an offline machine still answers.
/// </summary>
public sealed partial class AuthenticodeService : IAuthenticodeService
{
    private const int TRUST_E_NOSIGNATURE = unchecked((int)0x800B0100);
    private const int TRUST_E_SUBJECT_FORM_UNKNOWN = unchecked((int)0x800B0003);
    private const int TRUST_E_PROVIDER_UNKNOWN = unchecked((int)0x800B0001);

    public SignatureInfo Check(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        try
        {
            if (!File.Exists(path))
                return SignatureInfo.Unknown;

            var embedded = AuthenticodeVerifier.WinVerifyTrustFile(path);
            if (embedded == 0)
                return new SignatureInfo(SignatureState.Verified, SignerName(path) ?? "Unknown signer");
            if (embedded is not (TRUST_E_NOSIGNATURE or TRUST_E_SUBJECT_FORM_UNKNOWN or TRUST_E_PROVIDER_UNKNOWN))
                return new SignatureInfo(SignatureState.NotVerified, SignerName(path));

            var catalog = CatalogSigner(path);
            return catalog ?? SignatureInfo.Unsigned;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or CryptographicException)
        {
            return SignatureInfo.Unknown;
        }
    }

    /// <summary>The certificate's common name ("Microsoft Windows", "Adobe Inc.").</summary>
    private static string? SignerName(string signedFile)
    {
        try
        {
            using var certificate = new X509Certificate2(X509Certificate.CreateFromSignedFile(signedFile));
            var name = certificate.GetNameInfo(X509NameType.SimpleName, forIssuer: false);
            return string.IsNullOrWhiteSpace(name) ? null : name;
        }
        catch (CryptographicException)
        {
            return null;
        }
    }

    // ---- Catalog lookup (wintrust.dll CryptCAT* plus WinVerifyTrust with a catalog member) ----

    private static readonly Guid DriverActionVerify = new("F750E6C3-38EE-11d1-85E5-00C04FC295EE");
    private static readonly Guid ActionGenericVerifyV2 = new("00AAC56B-CD44-11d0-8CC2-00C04FC295EE");

    private const uint WTD_UI_NONE = 2;
    private const uint WTD_REVOKE_NONE = 0;
    private const uint WTD_CHOICE_CATALOG = 2;
    private const uint WTD_STATEACTION_VERIFY = 1;
    private const uint WTD_STATEACTION_CLOSE = 2;
    private const uint WTD_CACHE_ONLY_URL_RETRIEVAL = 0x00001000;

    private static unsafe SignatureInfo? CatalogSigner(string path)
    {
        nint hCatAdmin = 0;
        nint hCatInfo = 0;
        try
        {
            var subsystem = DriverActionVerify;
            if (!CryptCATAdminAcquireContext2(out hCatAdmin, in subsystem, "SHA256", 0, 0))
                return null;

            using var file = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            var handle = file.SafeFileHandle.DangerousGetHandle();
            uint hashSize = 0;
            if (!CryptCATAdminCalcHashFromFileHandle2(hCatAdmin, handle, ref hashSize, null, 0) || hashSize == 0)
                return null;
            var hash = new byte[hashSize];
            fixed (byte* hashPtr = hash)
            {
                if (!CryptCATAdminCalcHashFromFileHandle2(hCatAdmin, handle, ref hashSize, hashPtr, 0))
                    return null;
                hCatInfo = CryptCATAdminEnumCatalogFromHash(hCatAdmin, hashPtr, hashSize, 0, 0);
                if (hCatInfo == 0)
                    return null;

                var info = new CATALOG_INFO { cbStruct = (uint)sizeof(CATALOG_INFO) };
                if (!CryptCATCatalogInfoFromContext(hCatInfo, &info, 0))
                    return null;
                var catalogPath = new string(info.wszCatalogFile);

                var memberTag = Convert.ToHexString(hash);
                fixed (char* catalogPtr = catalogPath)
                fixed (char* tagPtr = memberTag)
                fixed (char* pathPtr = path)
                {
                    var catalogInfo = new WINTRUST_CATALOG_INFO
                    {
                        cbStruct = (uint)sizeof(WINTRUST_CATALOG_INFO),
                        pcwszCatalogFile = (nint)catalogPtr,
                        pcwszMemberTag = (nint)tagPtr,
                        pcwszMemberFilePath = (nint)pathPtr,
                        hMemberFile = handle,
                        pbCalculatedFileHash = (nint)hashPtr,
                        cbCalculatedFileHash = hashSize,
                        hCatAdmin = hCatAdmin,
                    };
                    var data = new WINTRUST_DATA
                    {
                        cbStruct = (uint)sizeof(WINTRUST_DATA),
                        dwUIChoice = WTD_UI_NONE,
                        fdwRevocationChecks = WTD_REVOKE_NONE,
                        dwUnionChoice = WTD_CHOICE_CATALOG,
                        pCatalog = (nint)(&catalogInfo),
                        dwStateAction = WTD_STATEACTION_VERIFY,
                        dwProvFlags = WTD_CACHE_ONLY_URL_RETRIEVAL,
                    };
                    var action = ActionGenericVerifyV2;
                    var result = WinVerifyTrust(0, ref action, &data);
                    data.dwStateAction = WTD_STATEACTION_CLOSE;
                    _ = WinVerifyTrust(0, ref action, &data);

                    var signer = SignerName(catalogPath);
                    return result == 0
                        ? new SignatureInfo(SignatureState.Verified, signer ?? "Unknown signer")
                        : new SignatureInfo(SignatureState.NotVerified, signer);
                }
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return null;
        }
        finally
        {
            if (hCatInfo != 0)
                CryptCATAdminReleaseCatalogContext(hCatAdmin, hCatInfo, 0);
            if (hCatAdmin != 0)
                CryptCATAdminReleaseContext(hCatAdmin, 0);
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    private unsafe struct CATALOG_INFO
    {
        public uint cbStruct;
        public fixed char wszCatalogFile[260];
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct WINTRUST_CATALOG_INFO
    {
        public uint cbStruct;
        public uint dwCatalogVersion;
        public nint pcwszCatalogFile;
        public nint pcwszMemberTag;
        public nint pcwszMemberFilePath;
        public nint hMemberFile;
        public nint pbCalculatedFileHash;
        public uint cbCalculatedFileHash;
        public nint pcCatalogContext;
        public nint hCatAdmin;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct WINTRUST_DATA
    {
        public uint cbStruct;
        public nint pPolicyCallbackData;
        public nint pSIPClientData;
        public uint dwUIChoice;
        public uint fdwRevocationChecks;
        public uint dwUnionChoice;
        public nint pCatalog;
        public uint dwStateAction;
        public nint hWVTStateData;
        public nint pwszURLReference;
        public uint dwProvFlags;
        public uint dwUIContext;
        public nint pSignatureSettings;
    }

    [LibraryImport("wintrust.dll", StringMarshalling = StringMarshalling.Utf16)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool CryptCATAdminAcquireContext2(out nint phCatAdmin, in Guid pgSubsystem, string pwszHashAlgorithm, nint pStrongHashPolicy, uint dwFlags);

    [LibraryImport("wintrust.dll")]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static unsafe partial bool CryptCATAdminCalcHashFromFileHandle2(nint hCatAdmin, nint hFile, ref uint pcbHash, byte* pbHash, uint dwFlags);

    [LibraryImport("wintrust.dll")]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    private static unsafe partial nint CryptCATAdminEnumCatalogFromHash(nint hCatAdmin, byte* pbHash, uint cbHash, uint dwFlags, nint phPrevCatInfo);

    [LibraryImport("wintrust.dll")]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static unsafe partial bool CryptCATCatalogInfoFromContext(nint hCatInfo, CATALOG_INFO* psCatInfo, uint dwFlags);

    [LibraryImport("wintrust.dll")]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool CryptCATAdminReleaseCatalogContext(nint hCatAdmin, nint hCatInfo, uint dwFlags);

    [LibraryImport("wintrust.dll")]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool CryptCATAdminReleaseContext(nint hCatAdmin, uint dwFlags);

    [LibraryImport("wintrust.dll")]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    private static unsafe partial int WinVerifyTrust(nint hwnd, ref Guid actionId, WINTRUST_DATA* pWVTData);
}
