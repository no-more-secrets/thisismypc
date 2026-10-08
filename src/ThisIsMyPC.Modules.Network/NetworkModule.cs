using ThisIsMyPC.Core.Changes;
using ThisIsMyPC.Core.Modules;
using ThisIsMyPC.Core.Network;
using ThisIsMyPC.Core.Results;

namespace ThisIsMyPC.Modules.Network;

public sealed class NetworkModule(INetworkAdapterService adapters, IFirewallService firewall) : IModule
{
    public ModuleInfo Info { get; } = new(NetworkChanges.ModuleName, "network",
        "Connections, DNS servers, and Windows Firewall", [SystemCapability.NativeApi, SystemCapability.Com], ModuleGroup.System, 6);
    public Task<ModuleAvailability> CheckAvailabilityAsync() => Task.FromResult(new ModuleAvailability(true));
    public Task<OperationResult<object>> ScanSystemStateAsync() => Task.Run(() =>
    {
        IReadOnlyList<NetworkAdapterState> interfaces = [];
        string? adapterError = null;
        try { interfaces = adapters.Read(); }
        catch (Exception ex) { adapterError = ex.Message; }
        FirewallState state;
        try { state = firewall.Read(); }
        catch (Exception ex) { state = new([], [], ex.Message); }
        return OperationResult<object>.Success(new NetworkScanData(interfaces, state, adapterError));
    });

    public Task<OperationResult<bool>> ApplyChangeAsync(ChangeDescriptor change) => Task.Run(() =>
    {
        if (!NetworkChanges.Allows(change))
            return OperationResult<bool>.Failure("The network change is outside the supported schema.", ErrorCategory.AccessDenied);
        var parts = change.SettingId.Split('/');
        return parts[0] switch
        {
            "adapter" => adapters.SetEnabled(Guid.Parse(parts[1]), change.BeforeValue == "true", change.AfterValue == "true"),
            "dns" => adapters.SetDns(Guid.Parse(parts[1]), change.BeforeValue, change.AfterValue!),
            "firewall" => firewall.SetEnabled(int.Parse(parts[1], System.Globalization.CultureInfo.InvariantCulture), change.BeforeValue == "true", change.AfterValue == "true"),
            _ => OperationResult<bool>.Failure("Unknown network target.", ErrorCategory.NotFound),
        };
    });
    // History and pending changes supply the inverted descriptor for undo and rollback.
    public Task<OperationResult<bool>> RevertChangeAsync(ChangeDescriptor change) => ApplyChangeAsync(change);
}
