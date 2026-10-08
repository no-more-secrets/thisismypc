using ThisIsMyPC.Core.Search;

namespace ThisIsMyPC.Modules.Network;

public sealed class NetworkSearchContributor : ISearchSettingsContributor
{
    public string ModuleId => NetworkChanges.ModuleName;
    public IReadOnlyList<SearchEntry> GetSearchEntries() =>
    [
        new(ModuleId, "connections", "Network connections", "Adapter status, addresses, and connection details", ["Ethernet", "Wi-Fi", "IP", "gateway", "network adapter"]),
        new(ModuleId, "dns", "DNS servers", "Automatic or manual IPv4 DNS servers", ["DNS", "name resolution"]),
        new(ModuleId, "firewall", "Windows Firewall", "Firewall profiles and existing application rules", ["firewall", "ports", "inbound", "outbound"]),
    ];
}
