using System.Collections.Concurrent;
using ThisIsMyPC.Core.Changes;
using ThisIsMyPC.Core.Modules;
using ThisIsMyPC.Core.Results;

namespace ThisIsMyPC.Core.Tests.Fakes;

public sealed class FakeModule : IModule
{
    private readonly ConcurrentDictionary<string, string> _state = new();

    public ModuleInfo Info { get; } = new(
        Name: "FakeModule",
        Icon: "test-icon",
        Description: "A fake module for testing the IModule contract",
        RequiredCapabilities: [SystemCapability.Registry]);

    public Task<ModuleAvailability> CheckAvailabilityAsync()
    {
        return Task.FromResult(new ModuleAvailability(IsAvailable: true));
    }

    public Task<OperationResult<object>> ScanSystemStateAsync()
    {
        var snapshot = new Dictionary<string, string>(_state);
        return Task.FromResult(OperationResult<object>.Success(snapshot));
    }

    public Task<OperationResult<bool>> ApplyChangeAsync(ChangeDescriptor change)
    {
        if (change.AfterValue is null)
        {
            _state.TryRemove(change.SettingId, out _);
        }
        else
        {
            _state[change.SettingId] = change.AfterValue;
        }

        return Task.FromResult(OperationResult<bool>.Success(true));
    }

    public Task<OperationResult<bool>> RevertChangeAsync(ChangeDescriptor change)
    {
        _state[change.SettingId] = change.BeforeValue;
        return Task.FromResult(OperationResult<bool>.Success(true));
    }

    public IReadOnlyDictionary<string, string> GetCurrentState() => _state;
}
