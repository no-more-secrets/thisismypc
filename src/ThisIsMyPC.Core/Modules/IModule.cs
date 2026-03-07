using ThisIsMyPC.Core.Changes;
using ThisIsMyPC.Core.Results;

namespace ThisIsMyPC.Core.Modules;

public interface IModule : IAsyncDisposable
{
    ModuleInfo Info { get; }
    Task<ModuleAvailability> CheckAvailabilityAsync();
    Task<OperationResult<object>> ScanSystemStateAsync();
    Task<OperationResult<bool>> ApplyChangeAsync(ChangeDescriptor change);
    Task<OperationResult<bool>> RevertChangeAsync(ChangeDescriptor change);

#pragma warning disable CA1816, CA1033 // Default no-op disposal per architecture contract
    ValueTask IAsyncDisposable.DisposeAsync() => ValueTask.CompletedTask;
#pragma warning restore CA1816, CA1033
}
