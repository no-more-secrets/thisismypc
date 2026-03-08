using ThisIsMyPC.Core.Changes;
using ThisIsMyPC.Core.Modules;
using ThisIsMyPC.Core.Results;
using ThisIsMyPC.Core.Services;
using ThisIsMyPC.Interop.Com.Shell;
using ThisIsMyPC.Modules.Shell.Models;
using ThisIsMyPC.Modules.Shell.Services;

namespace ThisIsMyPC.Modules.Shell;

public sealed class ShellModule : IModule
{

    private readonly IRegistryService _registryService;
    private readonly ExplorerSettingsReader _explorerSettingsReader;
    private readonly TaskbarSettingsReader _taskbarSettingsReader;
    private readonly NotificationSettingsReader _notificationSettingsReader;
    private readonly EnvironmentVariableReader _environmentVariableReader;
    private readonly ContextMenuScanner _contextMenuScanner;

    public ShellModule(
        IRegistryService registryService,
        IShellExtensionService shellExtensionService)
    {
        _registryService = registryService;
        _explorerSettingsReader = new ExplorerSettingsReader(registryService);
        _taskbarSettingsReader = new TaskbarSettingsReader(registryService);
        _notificationSettingsReader = new NotificationSettingsReader(registryService);
        _environmentVariableReader = new EnvironmentVariableReader(registryService);
        _contextMenuScanner = new ContextMenuScanner(shellExtensionService);
    }

    public ModuleInfo Info { get; } = new(
        Name: "Shell & Explorer",
        Icon: "shell",
        Description: "Customize Windows shell, context menus, taskbar, and Explorer settings",
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
                // Scan each section independently so partial results are returned on failure
                IReadOnlyList<ContextMenuHandler> contextMenuHandlers = [];
                try { contextMenuHandlers = _contextMenuScanner.Scan(); }
                catch (Exception ex) { System.Diagnostics.Debug.WriteLine($"Context menu scan failed: {ex.Message}"); }

                var explorerPreferences = _explorerSettingsReader.ReadAll();
                var taskbar = _taskbarSettingsReader.Read();
                var notificationSettings = _notificationSettingsReader.ReadAll();

                IReadOnlyList<EnvironmentVariable> userEnvVars = [];
                try { userEnvVars = _environmentVariableReader.ReadUserVariables(); }
                catch (Exception ex) { System.Diagnostics.Debug.WriteLine($"User env var scan failed: {ex.Message}"); }

                IReadOnlyList<EnvironmentVariable> systemEnvVars = [];
                try { systemEnvVars = _environmentVariableReader.ReadSystemVariables(); }
                catch (Exception ex) { System.Diagnostics.Debug.WriteLine($"System env var scan failed: {ex.Message}"); }

                var scanData = new ShellScanData(
                    ContextMenuHandlers: contextMenuHandlers,
                    ExplorerPreferences: explorerPreferences,
                    Taskbar: taskbar,
                    NotificationSettings: notificationSettings,
                    UserEnvironmentVariables: userEnvVars,
                    SystemEnvironmentVariables: systemEnvVars);

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
            // Special case: classic context menu toggle (key presence-based)
            if (change.SystemLocation == ShellRegistryPaths.ClassicContextMenuKeyPath)
            {
                return Task.FromResult(ApplyClassicContextMenuChange(change));
            }

            var result = change.ValueType switch
            {
                ChangeValueType.Registry_DWord => ApplyDWordChange(change),
                ChangeValueType.Registry_String => ApplyStringChange(change),
                ChangeValueType.Registry_ExpandString => ApplyExpandStringChange(change),
                ChangeValueType.Environment_Variable => OperationResult<bool>.Failure(
                    $"Environment variable changes are not yet implemented (Story 2.5)",
                    ErrorCategory.ServiceUnavailable),
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
        // PendingChangesService constructs a swapped descriptor — just apply it
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

    private OperationResult<bool> ApplyClassicContextMenuChange(ChangeDescriptor change)
    {
        if (change.AfterValue == ShellRegistryPaths.AbsentValue)
        {
            // Disable: delete the CLSID key tree
            var parentKeyPath = ShellRegistryPaths.ClassicContextMenuClsidKeyPath;
            return _registryService.DeleteKey(parentKeyPath, recursive: true);
        }
        else
        {
            // Enable: create the InprocServer32 key with empty Default value
            return _registryService.WriteString(ShellRegistryPaths.ClassicContextMenuKeyPath, string.Empty, string.Empty);
        }
    }

    private static (string KeyPath, string ValueName) ParseSystemLocation(string systemLocation)
    {
        var lastSep = systemLocation.LastIndexOf('\\');
        if (lastSep < 0)
            throw new ArgumentException($"Invalid system location (no separator): {systemLocation}");

        var valueName = systemLocation[(lastSep + 1)..];

        // Map "(Default)" to empty string — the Windows registry API convention for the default value
        if (valueName == "(Default)")
            valueName = string.Empty;

        return (systemLocation[..lastSep], valueName);
    }
}
