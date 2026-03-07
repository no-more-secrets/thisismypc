using ThisIsMyPC.Core.Changes;
using ThisIsMyPC.Core.Modules;
using ThisIsMyPC.Core.Results;

namespace ThisIsMyPC.Modules.Power;

public sealed class PowerModule : IModule
{
    public ModuleInfo Info { get; } = new(
        Name: "Power Plans",
        Icon: "power",
        Description: "Discover, switch, and adjust power plan settings",
        RequiredCapabilities: [SystemCapability.NativeApi],
        Group: ModuleGroup.Core,
        LoadOrder: 3);

    public Task<ModuleAvailability> CheckAvailabilityAsync()
    {
        return Task.FromResult(new ModuleAvailability(IsAvailable: true));
    }

    public Task<OperationResult<object>> ScanSystemStateAsync()
    {
        return Task.FromResult(OperationResult<object>.Success(new object()));
    }

    public Task<OperationResult<bool>> ApplyChangeAsync(ChangeDescriptor change)
    {
        return Task.FromResult(OperationResult<bool>.Success(true));
    }

    public Task<OperationResult<bool>> RevertChangeAsync(ChangeDescriptor change)
    {
        return Task.FromResult(OperationResult<bool>.Success(true));
    }
}
