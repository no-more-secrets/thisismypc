using ThisIsMyPC.Core.Changes;
using ThisIsMyPC.Core.Modules;
using ThisIsMyPC.Core.Results;
using ThisIsMyPC.Core.Services;
using ThisIsMyPC.Modules.Shell.Models;
using ThisIsMyPC.Modules.Shell.Services;

namespace ThisIsMyPC.Modules.Shell;

public sealed class ShellModule : IModule
{

    private readonly IShellNativeSettings _native;
    private readonly IRegistryService _registryService;
    private readonly ExplorerSettingsReader _explorerSettingsReader;
    private readonly TaskbarSettingsReader _taskbarSettingsReader;

    public ShellModule(IRegistryService registryService, IShellNativeSettings? native = null)
    {
        _registryService = registryService;
        _native = native ?? new Interop.Win32.Shell.ShellNativeSettings();
        _explorerSettingsReader = new ExplorerSettingsReader(registryService);
        _taskbarSettingsReader = new TaskbarSettingsReader(registryService);
    }

    public ModuleInfo Info { get; } = new(
        Name: "Explorer",
        Icon: "shell",
        Description: "Customize Windows Explorer, taskbar, and shell settings",
        RequiredCapabilities: [SystemCapability.Registry, SystemCapability.Com],
        Group: ModuleGroup.Core,
        LoadOrder: 1);

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
                var explorerPreferences = _explorerSettingsReader.ReadAll();
                var taskbar = _taskbarSettingsReader.Read();

                // ExplorerPatcher's settings only mean something while it is
                // installed and hooked into Explorer, so they are read only then.
                var explorerPatcherReader = new ExplorerPatcherSettingsReader(_registryService);
                var explorerPatcherInstalled = explorerPatcherReader.IsInstalled();

                var scanData = new ShellScanData(
                    ExplorerPreferences: explorerPreferences,
                    Taskbar: taskbar,
                    ExplorerPatcherSettings: explorerPatcherInstalled ? explorerPatcherReader.ReadAll() : [],
                    ExplorerPatcherInstalled: explorerPatcherInstalled,
                    ExplorerPatcherVersion: explorerPatcherInstalled ? explorerPatcherReader.InstalledVersion() : "",
                    ExplorerPatcherCatalogVersion: ExplorerPatcherCatalog.Version)
                {
                    TaskbarAutoHideState = _native.ReadTaskbarState().Value,
                    RoundedCornersState = explorerPatcherInstalled ? _native.ReadRoundedCornersState().Value : null,
                    ShellExtensionRegistered = Changes.ShellIntegrationChanges.IsRegistered(_registryService),
                };

                return OperationResult<object>.Success(scanData);
            }
            catch (Exception ex)
            {
                return OperationResult<object>.Failure(
                    $"Shell scan failed: {ex.Message}",
                    ErrorCategory.ServiceUnavailable, ex);
            }
        }).ConfigureAwait(false);
    }

    public Task<OperationResult<bool>> ApplyChangeAsync(ChangeDescriptor change)
    {
        try
        {
            if (change.ValueType == ChangeValueType.Shell_NativeSetting)
            {
                if (!Changes.ShellIntegrationChanges.Allows(change))
                    return Task.FromResult(OperationResult<bool>.Failure("Invalid shell operation.", ErrorCategory.AccessDenied));
                return change.SystemLocation == Changes.ShellIntegrationChanges.Taskbar
                    ? Task.FromResult(_native.WriteTaskbarState(change.AfterValue!))
                    : _native.WriteRoundedCornersStateAsync(change.AfterValue!);
            }
            if ((change.SettingId.StartsWith(Changes.ShellIntegrationChanges.Registration, StringComparison.Ordinal)
                    || change.SettingId == Changes.ShellIntegrationChanges.Navigation)
                && !Changes.ShellIntegrationChanges.Allows(change))
                return Task.FromResult(OperationResult<bool>.Failure("Invalid shell registration.", ErrorCategory.AccessDenied));
            // CLSID InprocServer32 override toggles (key presence-based, not value-based)
            if (change.SystemLocation == ShellRegistryPaths.ClassicContextMenuKeyPath)
            {
                return Task.FromResult(ApplyClsidOverride(change,
                    ShellRegistryPaths.ClassicContextMenuClsidKeyPath,
                    ShellRegistryPaths.ClassicContextMenuKeyPath));
            }

            if (change.SystemLocation == ShellRegistryPaths.CommandBarKeyPath)
            {
                return Task.FromResult(ApplyClsidOverride(change,
                    ShellRegistryPaths.CommandBarClsidKeyPath,
                    ShellRegistryPaths.CommandBarKeyPath));
            }

            // AbsentValue restores "value absent" for delete-to-restore preferences
            // (shortcut-suffix); the CLSID key-presence toggles are handled above.
            if (change.AfterValue == ShellRegistryPaths.AbsentValue
                || change.AfterValue == Changes.ShellIntegrationChanges.AbsentRegistrationKey
                    && (change.SettingId.StartsWith(Changes.ShellIntegrationChanges.Registration, StringComparison.Ordinal)
                        || change.SettingId == Changes.ShellIntegrationChanges.Navigation))
            {
                var (absentKeyPath, absentValueName) = ParseSystemLocation(change.SystemLocation);
                var deleted = _registryService.DeleteValue(absentKeyPath, absentValueName);
                if (deleted.IsSuccess && change.AfterValue == Changes.ShellIntegrationChanges.AbsentRegistrationKey
                    && _registryService.EnumerateValues(absentKeyPath) is { IsSuccess: true, Value.Count: 0 }
                    && _registryService.EnumerateSubKeys(absentKeyPath) is { IsSuccess: true, Value.Count: 0 })
                    deleted = _registryService.DeleteKey(absentKeyPath);
                return Task.FromResult(deleted);
            }

            var result = change.ValueType switch
            {
                ChangeValueType.Registry_DWord => ApplyDWordChange(change),
                ChangeValueType.Registry_String => ApplyStringChange(change),
                ChangeValueType.Registry_ExpandString => ApplyExpandStringChange(change),
                _ => OperationResult<bool>.Failure(
                    $"Unsupported value type: {change.ValueType}",
                    ErrorCategory.ServiceUnavailable),
            };

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
        // The revert contract is "apply the descriptor's AfterValue": both
        // ChangeHistoryService undo and PendingChangesService mid-group rollback
        // hand this a Before/After-swapped descriptor.
        return ApplyChangeAsync(change);
    }

    private OperationResult<bool> ApplyDWordChange(ChangeDescriptor change)
    {
        var (keyPath, valueName) = ParseSystemLocation(change.SystemLocation);
        if (!int.TryParse(change.AfterValue, out var intValue))
        {
            return OperationResult<bool>.Failure(
                $"Cannot parse DWord value '{change.AfterValue}' for {change.SystemLocation}",
                ErrorCategory.ServiceUnavailable);
        }

        return _registryService.WriteDWord(keyPath, valueName, intValue);
    }

    private OperationResult<bool> ApplyStringChange(ChangeDescriptor change)
    {
        var (keyPath, valueName) = ParseSystemLocation(change.SystemLocation);
        return _registryService.WriteString(keyPath, valueName, change.AfterValue ?? string.Empty);
    }

    private OperationResult<bool> ApplyExpandStringChange(ChangeDescriptor change)
    {
        var (keyPath, valueName) = ParseSystemLocation(change.SystemLocation);
        return _registryService.WriteExpandString(keyPath, valueName, change.AfterValue ?? string.Empty);
    }

    private OperationResult<bool> ApplyClsidOverride(ChangeDescriptor change, string clsidKeyPath, string inprocKeyPath)
    {
        if (change.AfterValue == ShellRegistryPaths.AbsentValue)
        {
            // Disable override: delete the CLSID key tree (restores default behavior)
            return _registryService.DeleteKey(clsidKeyPath, recursive: true);
        }
        else
        {
            // Enable override: create InprocServer32 key with empty Default value (nullifies the COM class)
            return _registryService.WriteString(inprocKeyPath, string.Empty, string.Empty);
        }
    }

    private static (string KeyPath, string ValueName) ParseSystemLocation(string systemLocation)
        => ShellRegistryPaths.ParseSystemLocation(systemLocation);
}
