using ThisIsMyPC.Core.Changes;
using ThisIsMyPC.Core.Modules;
using ThisIsMyPC.Core.Results;
using ThisIsMyPC.Core.Services;
using ThisIsMyPC.Modules.WindowsUpdate.Services;

namespace ThisIsMyPC.Modules.WindowsUpdate;

public sealed class WindowsUpdateModule : IModule
{
    private readonly IRegistryService _registryService;
    private readonly WindowsUpdateSettingsReader _settingsReader;

    public WindowsUpdateModule(IRegistryService registryService)
    {
        _registryService = registryService;
        _settingsReader = new WindowsUpdateSettingsReader(registryService);
    }

    public ModuleInfo Info { get; } = new(
        Name: "Windows Update",
        Icon: "windows-update",
        Description: "Tame update installs, forced restarts, driver overwrites, and feature upgrades",
        RequiredCapabilities: [SystemCapability.Registry],
        Group: ModuleGroup.System,
        LoadOrder: 2);

    public Task<ModuleAvailability> CheckAvailabilityAsync()
    {
        // HKLM policy-hive writes; the app runs elevated.
        return Task.FromResult(new ModuleAvailability(IsAvailable: true));
    }

    public async Task<OperationResult<object>> ScanSystemStateAsync()
    {
        return await Task.Run(() =>
        {
            try
            {
                return OperationResult<object>.Success((object)_settingsReader.ReadAll());
            }
            catch (Exception ex)
            {
                return OperationResult<object>.Failure(
                    $"Windows Update policy scan failed: {ex.Message}",
                    ErrorCategory.ServiceUnavailable, ex);
            }
        }).ConfigureAwait(false);
    }

    /// <summary>Empty AfterValue restores "value absent" (policy Not configured), like PowerModule.</summary>
    public Task<OperationResult<bool>> ApplyChangeAsync(ChangeDescriptor change)
    {
        try
        {
            var (keyPath, valueName) = WindowsUpdateRegistryPaths.ParseSystemLocation(change.SystemLocation);

            if (string.IsNullOrEmpty(change.AfterValue))
                return Task.FromResult(_registryService.DeleteValue(keyPath, valueName));

            if (change.ValueType == ChangeValueType.Registry_String)
            {
                return Task.FromResult(
                    _registryService.WriteString(keyPath, valueName, change.AfterValue));
            }

            if (change.ValueType != ChangeValueType.Registry_DWord)
            {
                return Task.FromResult(OperationResult<bool>.Failure(
                    $"Unsupported value type: {change.ValueType}",
                    ErrorCategory.ServiceUnavailable));
            }

            if (!int.TryParse(change.AfterValue, out var intValue))
            {
                return Task.FromResult(OperationResult<bool>.Failure(
                    $"Cannot parse DWord value '{change.AfterValue}' for {change.SystemLocation}",
                    ErrorCategory.ServiceUnavailable));
            }

            return Task.FromResult(_registryService.WriteDWord(keyPath, valueName, intValue));
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
        // The revert contract is "apply the descriptor's AfterValue": both
        // ChangeHistoryService undo and PendingChangesService mid-group rollback
        // hand this a Before/After-swapped descriptor. An empty original BeforeValue
        // (policy was absent) round-trips into a DeleteValue via ApplyChangeAsync.
        return ApplyChangeAsync(change);
    }
}
