using ThisIsMyPC.Core.Changes;
using ThisIsMyPC.Core.Modules;
using ThisIsMyPC.Core.Results;

namespace ThisIsMyPC.Integration.Tests.Fakes;

internal sealed class FakeModule : IModule
{
    private readonly Func<ChangeDescriptor, Task<OperationResult<bool>>>? _applyOverride;

    public FakeModule(string name = "FakeModule", Func<ChangeDescriptor, Task<OperationResult<bool>>>? applyOverride = null)
    {
        Info = new ModuleInfo(
            Name: name,
            Icon: "test",
            Description: "Fake module for testing",
            RequiredCapabilities: [],
            Group: ModuleGroup.Core,
            LoadOrder: 0);
        _applyOverride = applyOverride;
    }

    public ModuleInfo Info { get; }

    public Task<ModuleAvailability> CheckAvailabilityAsync()
        => Task.FromResult(new ModuleAvailability(IsAvailable: true));

    public Task<OperationResult<object>> ScanSystemStateAsync()
        => Task.FromResult(OperationResult<object>.Success(new object()));

    public Task<OperationResult<bool>> ApplyChangeAsync(ChangeDescriptor change)
        => _applyOverride?.Invoke(change) ?? Task.FromResult(OperationResult<bool>.Success(true));

    public Task<OperationResult<bool>> RevertChangeAsync(ChangeDescriptor change)
        => Task.FromResult(OperationResult<bool>.Success(true));
}
