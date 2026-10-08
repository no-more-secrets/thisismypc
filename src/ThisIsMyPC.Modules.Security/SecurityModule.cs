using ThisIsMyPC.Core.Changes;
using ThisIsMyPC.Core.Modules;
using ThisIsMyPC.Core.Results;
using ThisIsMyPC.Core.Services;

namespace ThisIsMyPC.Modules.Security;

public sealed class SecurityModule(IRegistryService registry) : IModule
{
    public ModuleInfo Info { get; } = new(SecurityCatalog.ModuleId, "privacy",
        "Sign-in, antivirus, app protection, and security notifications", [SystemCapability.Registry], ModuleGroup.System, 4);

    public Task<ModuleAvailability> CheckAvailabilityAsync() => Task.FromResult(new ModuleAvailability(true));
    public Task<OperationResult<object>> ScanSystemStateAsync() => Task.Run(() =>
        OperationResult<object>.Success(SecurityCatalog.Settings.Select(new SecuritySettings(registry).Read).ToList()));

    public Task<OperationResult<bool>> ApplyChangeAsync(ChangeDescriptor change)
    {
        if (!SecurityCatalog.Allows(change)) return Task.FromResult(OperationResult<bool>.Failure("Unsupported security change.", ErrorCategory.ProtectedByPolicy));
        var split = change.SystemLocation.LastIndexOf('\\');
        var key = change.SystemLocation[..split];
        var name = change.SystemLocation[(split + 1)..];
        var before = registry.ReadValue(key, name);
        var kind = change.ValueType == ChangeValueType.Registry_String ? RegistryValueDataKind.String : RegistryValueDataKind.DWord;
        var matches = change.BeforeValue == "" ? !before.IsSuccess && before.ErrorCategory == ErrorCategory.NotFound
            : before.IsSuccess && before.Value!.Kind == kind && before.Value.Data == change.BeforeValue;
        if (!matches) return Task.FromResult(OperationResult<bool>.Failure("The policy changed since it was queued. Refresh Security and try again.", ErrorCategory.ProtectedByPolicy));
        if (change.BeforeValue == change.AfterValue) return Task.FromResult(OperationResult<bool>.Success(true));
        var result = string.IsNullOrEmpty(change.AfterValue) ? registry.DeleteValue(key, name)
            : change.ValueType == ChangeValueType.Registry_String ? registry.WriteString(key, name, change.AfterValue)
            : registry.WriteDWord(key, name, int.Parse(change.AfterValue, System.Globalization.CultureInfo.InvariantCulture));
        if (!result.IsSuccess) return Task.FromResult(result);
        var after = registry.ReadValue(key, name);
        var verified = string.IsNullOrEmpty(change.AfterValue) ? !after.IsSuccess && after.ErrorCategory == ErrorCategory.NotFound
            : after.IsSuccess && after.Value!.Kind == kind && after.Value.Data == change.AfterValue;
        return Task.FromResult(verified ? OperationResult<bool>.Success(true)
            : OperationResult<bool>.Failure("Windows did not retain the requested policy value.", ErrorCategory.ProtectedByPolicy));
    }

    public Task<OperationResult<bool>> RevertChangeAsync(ChangeDescriptor change) => ApplyChangeAsync(change);
}
