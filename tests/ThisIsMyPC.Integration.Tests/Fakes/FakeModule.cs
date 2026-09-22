using ThisIsMyPC.Core.Changes;
using ThisIsMyPC.Core.Modules;
using ThisIsMyPC.Core.Results;

namespace ThisIsMyPC.Integration.Tests.Fakes;

internal sealed class FakeModule : IModule
{
    private readonly Func<ChangeDescriptor, Task<OperationResult<bool>>>? _applyOverride;

    private readonly bool _available;

    public FakeModule(string name = "FakeModule", Func<ChangeDescriptor, Task<OperationResult<bool>>>? applyOverride = null,
        ModuleGroup group = ModuleGroup.Core, bool available = true)
    {
        _available = available;
        Info = new ModuleInfo(
            Name: name,
            Icon: "test",
            Description: "Fake module for testing",
            RequiredCapabilities: [],
            Group: group,
            LoadOrder: 0);
        _applyOverride = applyOverride;
    }

    public ModuleInfo Info { get; }

    /// <summary>How many times the page asked this module for its live state.</summary>
    public int ScanCount { get; private set; }

    public Task<ModuleAvailability> CheckAvailabilityAsync()
        => Task.FromResult(_available
            ? new ModuleAvailability(IsAvailable: true)
            : new ModuleAvailability(IsAvailable: false, Reason: "Not on this PC."));

    public Task<OperationResult<object>> ScanSystemStateAsync()
    {
        ScanCount++;
        return Task.FromResult(OperationResult<object>.Success(new object()));
    }

    public Task<OperationResult<bool>> ApplyChangeAsync(ChangeDescriptor change)
        => _applyOverride?.Invoke(change) ?? Task.FromResult(OperationResult<bool>.Success(true));

    public Task<OperationResult<bool>> RevertChangeAsync(ChangeDescriptor change)
        => Task.FromResult(OperationResult<bool>.Success(true));
}
