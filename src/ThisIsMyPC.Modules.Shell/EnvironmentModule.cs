using ThisIsMyPC.Core.Changes;
using ThisIsMyPC.Core.Modules;
using ThisIsMyPC.Core.Results;
using ThisIsMyPC.Core.Services;
using ThisIsMyPC.Modules.Shell.Models;
using ThisIsMyPC.Modules.Shell.Services;

namespace ThisIsMyPC.Modules.Shell;

public sealed class EnvironmentModule : IModule
{
    private readonly IRegistryService _registryService;
    private readonly EnvironmentVariableReader _environmentVariableReader;
    private readonly IEnvironmentBroadcaster _environmentBroadcaster;

    public EnvironmentModule(IRegistryService registryService, IEnvironmentBroadcaster environmentBroadcaster)
    {
        _registryService = registryService;
        _environmentVariableReader = new EnvironmentVariableReader(registryService);
        _environmentBroadcaster = environmentBroadcaster;
    }

    public ModuleInfo Info { get; } = new(
        Name: "Environment",
        Icon: "environment",
        Description: "View, edit, and manage system and user environment variables",
        RequiredCapabilities: [SystemCapability.Registry],
        Group: ModuleGroup.Core,
        LoadOrder: 3);

    public Task<ModuleAvailability> CheckAvailabilityAsync()
    {
        return Task.FromResult(new ModuleAvailability(IsAvailable: true));
    }

    public async Task<OperationResult<object>> ScanSystemStateAsync()
    {
        return await Task.Run(() =>
        {
            try
            {
                IReadOnlyList<EnvironmentVariable> userVars = [];
                string? userError = null;
                try { userVars = _environmentVariableReader.ReadUserVariables(); }
                catch (Exception ex) { userError = ex.Message; }

                IReadOnlyList<EnvironmentVariable> systemVars = [];
                string? systemError = null;
                try { systemVars = _environmentVariableReader.ReadSystemVariables(); }
                catch (Exception ex) { systemError = ex.Message; }

                var scanData = new EnvironmentScanData(
                    UserVariables: userVars,
                    SystemVariables: systemVars,
                    UserScanError: userError,
                    SystemScanError: systemError);

                return OperationResult<object>.Success(scanData);
            }
            catch (Exception ex)
            {
                return OperationResult<object>.Failure(
                    $"Environment scan failed: {ex.Message}",
                    ErrorCategory.ServiceUnavailable, ex);
            }
        }).ConfigureAwait(false);
    }

    public Task<OperationResult<bool>> ApplyChangeAsync(ChangeDescriptor change)
    {
        try
        {
            if (change.ValueType != ChangeValueType.Environment_Variable)
            {
                return Task.FromResult(OperationResult<bool>.Failure(
                    $"Unsupported value type: {change.ValueType}",
                    ErrorCategory.ServiceUnavailable));
            }

            var (keyPath, valueName) = ShellRegistryPaths.ParseSystemLocation(change.SystemLocation);

            OperationResult<bool> result;

            if (change.Category == ChangeCategory.Delete)
            {
                result = _registryService.DeleteValue(keyPath, valueName);
                // Deleting an already-absent value is a no-op success
                if (!result.IsSuccess && result.ErrorCategory == ErrorCategory.NotFound)
                    result = OperationResult<bool>.Success(true);
            }
            else
            {
                // Create and Modify both write the value
                result = _registryService.WriteExpandString(keyPath, valueName, change.AfterValue ?? string.Empty);
            }

            if (result.IsSuccess)
            {
                _environmentBroadcaster.BroadcastEnvironmentChange();
            }

            return Task.FromResult(result);
        }
        catch (Exception ex)
        {
            return Task.FromResult(OperationResult<bool>.Failure(
                $"Failed to apply change '{change.DisplayName}': {ex.Message}",
                ErrorCategory.ServiceUnavailable, ex));
        }
    }

    public Task<OperationResult<bool>> RevertChangeAsync(ChangeDescriptor change)
    {
        return ApplyChangeAsync(change);
    }
}
