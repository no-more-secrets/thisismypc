using ThisIsMyPC.Core.Changes;
using ThisIsMyPC.Core.Modules;
using ThisIsMyPC.Core.Results;
using ThisIsMyPC.Core.Services;
using ThisIsMyPC.Modules.Privacy.Services;

namespace ThisIsMyPC.Modules.Privacy;

public sealed class PrivacyModule : IModule
{
    private readonly IRegistryService _registryService;
    private readonly PrivacySettingsReader _settingsReader;
    private readonly Core.Policies.ILocalPolicyService? _localPolicies;
    private readonly Core.Policies.PolicyControlStateReader? _policies;

    public PrivacyModule(IRegistryService registryService, Core.Policies.ILocalPolicyService? localPolicies = null,
        Core.Policies.PolicyControlStateReader? policies = null)
    {
        _registryService = registryService;
        _settingsReader = new PrivacySettingsReader(registryService);
        _localPolicies = localPolicies;
        _policies = policies;
    }

    public ModuleInfo Info { get; } = new(
        Name: "Privacy & Telemetry",
        Icon: "privacy",
        Description: "Limit diagnostic data, error reporting, tracking, and personalization data collection",
        RequiredCapabilities: [SystemCapability.Registry],
        Group: ModuleGroup.System,
        LoadOrder: 3);

    public Task<ModuleAvailability> CheckAvailabilityAsync()
    {
        // HKLM policy + HKCU value writes; the app runs elevated. The DiagTrack
        // companion goes through the enforcement executor, not this module.
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
                    $"Privacy scan failed: {ex.Message}",
                    ErrorCategory.ServiceUnavailable, ex);
            }
        }).ConfigureAwait(false);
    }

    /// <summary>Empty AfterValue restores "value absent" (policy Not configured), like PowerModule.</summary>
    public Task<OperationResult<bool>> ApplyChangeAsync(ChangeDescriptor change)
    {
        if (Core.Policies.LocalPolicyValue.IsPolicyType(change.ValueType))
        {
            if (!DiagnosticDataSetting.Allows(change))
                return Task.FromResult(OperationResult<bool>.Failure("Unsupported privacy policy change.", ErrorCategory.ProtectedByPolicy));
            try { DiagnosticDataSetting.VerifyUserPolicy(_registryService, _policies); }
            catch (InvalidOperationException ex)
            { return Task.FromResult(OperationResult<bool>.Failure(ex.Message, ErrorCategory.ProtectedByPolicy)); }
            return Task.Run(() => _localPolicies?.Apply(change.SystemLocation, change.ValueType,
                Core.Policies.LocalPolicyValue.Decode(change.BeforeValue)!, Core.Policies.LocalPolicyValue.Decode(change.AfterValue)!)
                ?? OperationResult<bool>.Failure("The local policy editor is unavailable.", ErrorCategory.ServiceUnavailable));
        }
        try
        {
            var (keyPath, valueName) = PrivacyRegistryPaths.ParseSystemLocation(change.SystemLocation);

            if (string.IsNullOrEmpty(change.AfterValue))
                return Task.FromResult(_registryService.DeleteValue(keyPath, valueName));

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
