using ThisIsMyPC.Core.Changes;
using ThisIsMyPC.Core.Modules;
using ThisIsMyPC.Core.Results;
using ThisIsMyPC.Core.Services;
using ThisIsMyPC.Modules.Annoyances.Models;
using ThisIsMyPC.Modules.Annoyances.Services;

namespace ThisIsMyPC.Modules.Annoyances;

public sealed class AnnoyancesModule : IModule
{
    private readonly IRegistryService _registryService;
    private readonly AnnoyancesSettingsReader _settingsReader;

    public AnnoyancesModule(IRegistryService registryService)
    {
        _registryService = registryService;
        _settingsReader = new AnnoyancesSettingsReader(registryService);
    }

    public ModuleInfo Info { get; } = new(
        Name: "Windows Annoyances",
        Icon: "annoyances",
        Description: "Suppress nag screens, suggestions, ads, and other Windows upsell attempts",
        RequiredCapabilities: [SystemCapability.Registry],
        Group: ModuleGroup.System,
        LoadOrder: 1);

    public Task<ModuleAvailability> CheckAvailabilityAsync()
    {
        // Standard registry writes (HKCU + the HKLM EdgeUpdate policy); the app runs elevated.
        return Task.FromResult(new ModuleAvailability(IsAvailable: true));
    }

    public async Task<OperationResult<object>> ScanSystemStateAsync()
    {
        return await Task.Run(() =>
        {
            try
            {
                var scanData = new AnnoyancesScanData(
                    _settingsReader.ReadAll(),
                    _settingsReader.ReadBingSearch(),
                    _settingsReader.ReadSettingsSuggestedContent());
                return OperationResult<object>.Success(scanData);
            }
            catch (Exception ex)
            {
                return OperationResult<object>.Failure(
                    $"Annoyances scan failed: {ex.Message}",
                    ErrorCategory.ServiceUnavailable, ex);
            }
        }).ConfigureAwait(false);
    }

    public Task<OperationResult<bool>> ApplyChangeAsync(ChangeDescriptor change)
    {
        try
        {
            var (keyPath, valueName) = AnnoyancesRegistryPaths.ParseSystemLocation(change.SystemLocation);

            if (change.ValueType == ChangeValueType.Registry_String)
            {
                return Task.FromResult(
                    _registryService.WriteString(keyPath, valueName, change.AfterValue ?? string.Empty));
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
        // hand this a Before/After-swapped descriptor.
        return ApplyChangeAsync(change);
    }
}
