using System.Runtime.InteropServices;
using Microsoft.Win32;
using ThisIsMyPC.Core.Drift;
using ThisIsMyPC.Core.Drift.Eligibility;

namespace ThisIsMyPC.Interop.Win32.Drift;

/// <summary>Fresh evidence for the saved owner. Failures and ambiguous management evidence block writes.</summary>
public sealed partial class NativeRestorationEvidence
{
    public RestorationLoopEvidence Read(RestorationIdentity identity)
    {
        var profile = RestorationProfileState.Unknown;
        var management = RestorationManagementState.Unknown;
        try
        {
            if (!new System.Security.Principal.SecurityIdentifier(identity.UserSid).IsAccountSid())
                return new(new(identity.UserSid, RestorationProfileState.Unsupported), new(identity, management));
            using var machine = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Registry64);
            using var users = RegistryKey.OpenBaseKey(RegistryHive.Users, RegistryView.Registry64);
            using var registration = machine.OpenSubKey(@"SOFTWARE\Microsoft\Windows NT\CurrentVersion\ProfileList\" + identity.UserSid);
            if (registration?.GetValue("ProfileImagePath") is not string path || string.IsNullOrWhiteSpace(path))
                return new(new(identity.UserSid, profile), new(identity, management));
            using var owner = users.OpenSubKey(identity.UserSid);
            profile = owner is null ? RestorationProfileState.Unloaded : RestorationProfileState.SupportedAndLoaded;
            if (owner is null) return new(new(identity.UserSid, profile), new(identity, management));
            // Device APIs report domain and MDM enrollment. Check the bound owner's workplace registration,
            // rather than mistaking SYSTEM's current-user registration for evidence about the owner.
            var joinResult = NetGetJoinInformation(0, out var name, out var join);
            if (name != 0) NetApiBufferFree(name);
            if (joinResult != 0 || join is not (1 or 2 or 3))
                return new(new(identity.UserSid, profile), new(identity, management));
            if (join == 3) return new(new(identity.UserSid, profile), new(identity, RestorationManagementState.Managed));
            var mdmResult = IsDeviceRegisteredWithManagement(out var enrolled, 0, 0);
            if (mdmResult != 0) return new(new(identity.UserSid, profile), new(identity, management));
            if (enrolled != 0 || HasData(machine, @"SYSTEM\CurrentControlSet\Control\CloudDomainJoin\JoinInfo") ||
                HasData(owner, @"Software\Microsoft\Windows NT\CurrentVersion\WorkplaceJoin\JoinInfo") ||
                HasData(owner, @"Software\Microsoft\Windows NT\CurrentVersion\WorkplaceJoin\JoinInfoCache"))
                return new(new(identity.UserSid, profile), new(identity, RestorationManagementState.Managed));
            foreach (var policyKey in RelatedPolicyKeys(identity.KeyPath))
                if (HasData(machine, policyKey) || HasData(owner, policyKey))
                    return new(new(identity.UserSid, profile), new(identity, RestorationManagementState.Managed));
            // Inspect relevant local policy records, including group-specific policy. Unrelated policies do not block.
            var system = Environment.GetFolderPath(Environment.SpecialFolder.System);
            var policyPaths = new[]
            {
                Path.Combine(system, "GroupPolicy", "User", "Registry.pol"),
                Path.Combine(system, "GroupPolicy", "Machine", "Registry.pol"),
                Path.Combine(system, "GroupPolicyUsers", identity.UserSid, "User", "Registry.pol"),
                Path.Combine(system, "GroupPolicyUsers", "S-1-5-32-544", "User", "Registry.pol"),
                Path.Combine(system, "GroupPolicyUsers", "S-1-5-32-545", "User", "Registry.pol"),
            };
            foreach (var policy in policyPaths)
            {
                try
                {
                    using var stream = new FileStream(policy, FileMode.Open, FileAccess.Read, FileShare.Read);
                    if (stream.Length > 4 * 1024 * 1024) return new(new(identity.UserSid, profile), new(identity, management));
                    var bytes = new byte[checked((int)stream.Length)];
                    stream.ReadExactly(bytes);
                    if (LocalPolicyTouchesTarget(bytes, identity.KeyPath))
                        return new(new(identity.UserSid, profile), new(identity, RestorationManagementState.Managed));
                }
                catch (FileNotFoundException) { }
                catch (DirectoryNotFoundException) { }
            }
            management = RestorationManagementState.Unmanaged;
        }
        catch (Exception ex) when (ex is IOException or InvalidDataException or UnauthorizedAccessException or System.Security.SecurityException
            or DllNotFoundException or EntryPointNotFoundException or ArgumentException)
        { management = RestorationManagementState.Unknown; }
        return new(new(identity.UserSid, profile), new(identity, management));
    }

    private static bool HasData(RegistryKey root, string path)
    {
        using var key = root.OpenSubKey(path);
        return key is not null && (key.SubKeyCount != 0 || key.ValueCount != 0);
    }

    /// <summary>Strict bounded Registry.pol parsing. Malformed input throws and remains unknown.</summary>
    public static bool LocalPolicyTouchesTarget(byte[] bytes, string keyPath)
    {
        using var input = new MemoryStream(bytes, writable: false);
        using var reader = new BinaryReader(input, System.Text.Encoding.Unicode);
        if (reader.ReadUInt32() != 0x67655250 || reader.ReadUInt32() != 1) throw new InvalidDataException("Invalid local policy header.");
        var target = keyPath.StartsWith("HKCU\\", StringComparison.OrdinalIgnoreCase) ? keyPath[5..] : keyPath;
        var relevant = false;
        while (input.Position < input.Length)
        {
            Require('[');
            var key = ReadText(); Require(';');
            _ = ReadText(); Require(';');
            _ = reader.ReadUInt32(); Require(';');
            var size = reader.ReadUInt32(); Require(';');
            if (size > input.Length - input.Position) throw new InvalidDataException("Invalid local policy data size.");
            input.Position += size;
            Require(']');
            // Related policy branches may supersede these preference choices. Conservatively treat the whole
            // related branch as managed, rather than assuming that only one policy value has an effect.
            relevant |= Related(key, target) || RelatedPolicyKeys(keyPath).Any(policyKey => Related(key, policyKey));
        }
        return relevant;
        void Require(char expected)
        {
            if (reader.ReadUInt16() != expected) throw new InvalidDataException("Invalid local policy delimiter.");
        }
        string ReadText()
        {
            var value = new System.Text.StringBuilder();
            for (var i = 0; i < 32768; i++)
            {
                var c = reader.ReadUInt16();
                if (c == 0) return value.ToString();
                value.Append((char)c);
            }
            throw new InvalidDataException("Local policy field exceeds limit.");
        }
        static bool Related(string key, string targetKey) => string.Equals(key, targetKey, StringComparison.OrdinalIgnoreCase) ||
            key.StartsWith(targetKey + "\\", StringComparison.OrdinalIgnoreCase) || targetKey.StartsWith(key + "\\", StringComparison.OrdinalIgnoreCase);
    }

    private static IEnumerable<string> RelatedPolicyKeys(string target)
    {
        if (target.EndsWith("ContentDeliveryManager", StringComparison.OrdinalIgnoreCase) ||
            target.EndsWith("Privacy", StringComparison.OrdinalIgnoreCase))
            yield return @"Software\Policies\Microsoft\Windows\CloudContent";
        if (target.EndsWith("UserProfileEngagement", StringComparison.OrdinalIgnoreCase))
            yield return @"Software\Policies\Microsoft\Windows\OOBE";
        if (target.EndsWith("AdvertisingInfo", StringComparison.OrdinalIgnoreCase))
            yield return @"Software\Policies\Microsoft\Windows\AdvertisingInfo";
        if (target.EndsWith("SearchSettings", StringComparison.OrdinalIgnoreCase))
            yield return @"Software\Policies\Microsoft\Windows\Windows Search";
        if (target.EndsWith(@"International\User Profile", StringComparison.OrdinalIgnoreCase))
            yield return @"Software\Policies\Microsoft\Control Panel\International";
    }

    [LibraryImport("netapi32.dll")]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    private static partial int NetGetJoinInformation(nint server, out nint name, out int status);
    [LibraryImport("netapi32.dll")]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    private static partial int NetApiBufferFree(nint buffer);
    [LibraryImport("mdmregistration.dll")]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    private static partial int IsDeviceRegisteredWithManagement(out int enrolled, uint characters, nint user);
}
