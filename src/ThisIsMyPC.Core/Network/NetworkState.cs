using ThisIsMyPC.Core.Results;

namespace ThisIsMyPC.Core.Network;

/// <summary>One interface, including independent link, administrative, and DNS state.</summary>
public sealed record NetworkAdapterState(Guid Id, string Name, string Description, string Kind,
    string LinkState, string Addresses, string Gateways, string EffectiveDns, string MacAddress,
    long LinkSpeed, bool? Enabled, string? StaticDns, string? Error);

/// <summary>Windows Firewall profile state. Unknown is never represented as disabled.</summary>
public sealed record FirewallProfileState(int Id, string Name, bool Enabled, bool Active,
    string Inbound, string Outbound, bool CanModify);

/// <summary>Read-only firewall rule details reported by Windows.</summary>
public sealed record FirewallRuleState(string Name, string Application, string Service, bool Enabled,
    string Direction, string Action, string Protocol, string LocalPorts, string RemotePorts,
    string LocalAddresses, string RemoteAddresses, string Profiles);

public sealed record FirewallState(IReadOnlyList<FirewallProfileState> Profiles,
    IReadOnlyList<FirewallRuleState> Rules, string? Error = null);

public sealed record NetworkScanData(IReadOnlyList<NetworkAdapterState> Adapters, FirewallState Firewall,
    string? AdapterError = null);

/// <summary>Native adapter operations, scoped by stable interface GUID.</summary>
public interface INetworkAdapterService
{
    IReadOnlyList<NetworkAdapterState> Read();
    OperationResult<bool> SetEnabled(Guid id, bool expected, bool desired);
    OperationResult<bool> SetDns(Guid id, string expected, string desired);
}

/// <summary>Firewall inspection and narrowly scoped profile changes.</summary>
public interface IFirewallService
{
    FirewallState Read();
    OperationResult<bool> SetEnabled(int profile, bool expected, bool desired);
}
