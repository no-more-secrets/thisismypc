using System.ComponentModel;
using System.Net.NetworkInformation;
using System.Runtime.InteropServices;
using Microsoft.Win32;
using ThisIsMyPC.Core.Network;
using ThisIsMyPC.Core.Results;

namespace ThisIsMyPC.Interop.Win32.Network;

/// <summary>IP Helper operations. No command execution, WMI, or registry writes.</summary>
public sealed partial class NetworkAdapterService : INetworkAdapterService
{
    private const string Automatic = "<automatic>";

    public IReadOnlyList<NetworkAdapterState> Read()
    {
        var results = new List<NetworkAdapterState>();
        foreach (var adapter in NetworkInterface.GetAllNetworkInterfaces())
        {
            if (!Guid.TryParse(adapter.Id, out var id) || adapter.NetworkInterfaceType == NetworkInterfaceType.Loopback) continue;
            try
            {
                var properties = adapter.GetIPProperties();
                bool? enabled = null;
                string? dns = null;
                var errors = new List<string>();
                try { enabled = ReadEnabled(id); } catch (Exception ex) { errors.Add(ex.Message); }
                try { dns = ReadDns(id); } catch (Exception ex) { errors.Add(ex.Message); }
                results.Add(new(id, adapter.Name, adapter.Description, adapter.NetworkInterfaceType.ToString(),
                    adapter.OperationalStatus.ToString(), string.Join(", ", properties.UnicastAddresses.Select(a => a.Address)),
                    string.Join(", ", properties.GatewayAddresses.Select(a => a.Address)),
                    string.Join(", ", properties.DnsAddresses), adapter.GetPhysicalAddress().ToString(), adapter.Speed,
                    enabled, dns, errors.Count == 0 ? null : string.Join("; ", errors)));
            }
            catch (Exception ex)
            {
                results.Add(new(id, adapter.Name, adapter.Description, adapter.NetworkInterfaceType.ToString(),
                    "Unavailable", "", "", "", "", 0, null, null, ex.Message));
            }
        }
        return results.OrderByDescending(a => a.LinkState == "Up")
            .ThenBy(a => a.LinkState == "NotPresent")
            .ThenBy(a => a.Name, StringComparer.CurrentCultureIgnoreCase).ToArray();
    }

    public OperationResult<bool> SetEnabled(Guid id, bool expected, bool desired) => Try(() =>
    {
        var row = ReadRow(id);
        if ((row.AdminStatus == 1) != expected) throw new InvalidOperationException("Adapter state changed. Refresh before applying.");
        row.AdminStatus = desired ? 1u : 2u;
        Check(SetIfEntry(ref row));
    });

    public OperationResult<bool> SetDns(Guid id, string expected, string desired) => Try(() =>
    {
        if (ComparableDns(ReadDns(id)) != ComparableDns(expected)) throw new InvalidOperationException("DNS settings changed. Refresh before applying.");
        // Only the static IPv4 server list is changed. Suffixes, IPv6, and encryption options are preserved.
        var text = Marshal.StringToHGlobalUni(desired == Automatic ? "" : desired);
        try
        {
            var settings = new DnsSettings { Version = 1, Flags = 2, NameServer = text };
            Check(SetInterfaceDnsSettings(id, ref settings));
        }
        finally { Marshal.FreeHGlobal(text); }
    });

    private static string ReadDns(Guid id)
    {
        // Do not offer a preference edit when policy or profile-specific DNS can override it.
        foreach (var path in new[] { @"SOFTWARE\Policies\Microsoft\Windows NT\DNSClient", @"SYSTEM\CurrentControlSet\Services\Dnscache\Parameters\DnsPolicyConfig" })
        {
            using var key = Microsoft.Win32.Registry.LocalMachine.OpenSubKey(path);
            if (key is not null && (key.ValueCount > 0 || key.SubKeyCount > 0))
                throw new InvalidOperationException("DNS policy is present. Adapter DNS editing is unavailable.");
        }
        var settings = new DnsSettings { Version = 1 };
        Check(GetInterfaceDnsSettings(id, ref settings));
        try
        {
            if (!string.IsNullOrEmpty(Marshal.PtrToStringUni(settings.ProfileNameServer)))
                throw new InvalidOperationException("This connection has profile-specific DNS settings.");
            var servers = Marshal.PtrToStringUni(settings.NameServer);
            return string.IsNullOrWhiteSpace(servers) ? Automatic : servers;
        }
        finally { FreeInterfaceDnsSettings(ref settings); }
    }

    private static bool ReadEnabled(Guid id) => ReadRow(id).AdminStatus switch
    {
        1 => true, 2 => false, _ => throw new InvalidOperationException("Adapter administrative state is unavailable."),
    };

    private static string ComparableDns(string value) => value == Automatic ? value :
        string.Join(",", value.Split([' ', ',', ';', '\t', '\r', '\n'], StringSplitOptions.RemoveEmptyEntries));

    private static IfRow ReadRow(Guid id)
    {
        Check(ConvertInterfaceGuidToLuid(in id, out var luid));
        Check(ConvertInterfaceLuidToIndex(in luid, out var index));
        var row = new IfRow { Index = index };
        Check(GetIfEntry(ref row));
        return row;
    }

    private static OperationResult<bool> Try(Action action)
    {
        try { action(); return OperationResult<bool>.Success(true); }
        catch (Exception ex) { return OperationResult<bool>.Failure(ex.Message, ErrorCategory.ServiceUnavailable, ex); }
    }
    private static void Check(uint result) { if (result != 0) throw new Win32Exception(unchecked((int)result)); }

    // MIB_IFROW from ifmib.h. The full buffer is retained for GetIfEntry/SetIfEntry.
    [StructLayout(LayoutKind.Explicit, Size = 860)]
    private struct IfRow
    {
        [FieldOffset(512)] public uint Index;
        [FieldOffset(540)] public uint AdminStatus;
    }
    [StructLayout(LayoutKind.Sequential)]
    private struct DnsSettings
    {
        public uint Version;
        public ulong Flags;
        public nint Domain, NameServer, SearchList;
        public uint RegistrationEnabled, RegisterAdapterName, EnableLlmnr, QueryAdapterName;
        public nint ProfileNameServer;
    }
    [LibraryImport("iphlpapi.dll"), DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    private static partial uint ConvertInterfaceGuidToLuid(in Guid guid, out ulong luid);
    [LibraryImport("iphlpapi.dll"), DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    private static partial uint ConvertInterfaceLuidToIndex(in ulong luid, out uint index);
    [LibraryImport("iphlpapi.dll"), DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    private static partial uint GetIfEntry(ref IfRow row);
    [LibraryImport("iphlpapi.dll"), DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    private static partial uint SetIfEntry(ref IfRow row);
    [LibraryImport("iphlpapi.dll"), DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    private static partial uint GetInterfaceDnsSettings(Guid id, ref DnsSettings settings);
    [LibraryImport("iphlpapi.dll"), DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    private static partial uint SetInterfaceDnsSettings(Guid id, ref DnsSettings settings);
    [LibraryImport("iphlpapi.dll"), DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    private static partial void FreeInterfaceDnsSettings(ref DnsSettings settings);
}
