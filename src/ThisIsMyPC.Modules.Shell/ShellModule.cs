using ThisIsMyPC.Core.Changes;
using ThisIsMyPC.Core.Modules;
using ThisIsMyPC.Core.Results;

namespace ThisIsMyPC.Modules.Shell;

public sealed class ShellModule : IModule
{
    public ModuleInfo Info { get; } = new(
        Name: "Shell & Explorer",
        Icon: "shell",
        Description: "Customize Windows shell, context menus, taskbar, and Explorer settings",
        RequiredCapabilities: [SystemCapability.Registry, SystemCapability.Com],
        Group: ModuleGroup.Core,
        LoadOrder: 1);

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
