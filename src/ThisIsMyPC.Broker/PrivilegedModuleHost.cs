using System.Security.Principal;
using Microsoft.Extensions.DependencyInjection;
using ThisIsMyPC.App.Services;
using ThisIsMyPC.Core.Actions;
using ThisIsMyPC.Core.Changes;
using ThisIsMyPC.Core.Drift;
using ThisIsMyPC.Core.Enforcement;
using ThisIsMyPC.Core.Modules;
using ThisIsMyPC.Core.Packages;
using ThisIsMyPC.Core.Results;
using ThisIsMyPC.Core.Services;
using ThisIsMyPC.Interop.Com.Packages;
using ThisIsMyPC.Interop.Com.Shell;
using ThisIsMyPC.Interop.Com.Startup;
using ThisIsMyPC.Interop.Com.Tasks;
using ThisIsMyPC.Interop.Win32;
using ThisIsMyPC.Interop.Win32.Drift;
using ThisIsMyPC.Interop.Win32.Packages;
using ThisIsMyPC.Interop.Win32.Registry;
using ThisIsMyPC.Interop.Win32.Services;
using ThisIsMyPC.Modules.Annoyances;
using ThisIsMyPC.Modules.Power;
using ThisIsMyPC.Modules.Privacy;
using ThisIsMyPC.Modules.Shell;
using ThisIsMyPC.Modules.Startup;
using ThisIsMyPC.Modules.WindowsUpdate;

namespace ThisIsMyPC.Broker;

internal sealed class PrivilegedModuleHost : IAsyncDisposable
{
    private readonly ServiceProvider _services;
    private readonly Dictionary<string, IModule> _modules;
    private readonly ReversibleChangeExecutor _executor;
    private readonly DeliberateChangeCoordinator _deliberateChanges;
    private readonly NativeRestorationSession _restoration;
    private readonly string _uiUserSid;
    private readonly string? _brokerUserSid;

    internal PrivilegedModuleHost(string uiUserSid)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(uiUserSid);
        _uiUserSid = uiUserSid;
        using var identity = WindowsIdentity.GetCurrent();
        _brokerUserSid = identity.User?.Value;
        Directory.CreateDirectory(Core.AppConstants.DataDirectoryPath);
        var hardened = new DataDirectoryGuard().EnsureHardened(Core.AppConstants.DataDirectoryPath);
        if (!hardened.IsSuccess)
            throw new UnauthorizedAccessException(hardened.ErrorMessage);

        var services = new ServiceCollection();
        services.AddSingleton<IRegistryService, RegistryService>();
        services.AddSingleton<IEnvironmentBroadcaster, EnvironmentBroadcaster>();
        services.AddSingleton<IServiceControlService, ServiceControlService>();
        services.AddSingleton<IStartupFolderService, StartupFolderService>();
        services.AddSingleton<IScheduledTaskService, ScheduledTaskService>();
        services.AddSingleton<IInteractiveUserContext, DesktopUserContext>();
        services.AddSingleton<IAppxPackageService, AppxPackageService>();
        services.AddSingleton<IWingetService, WingetService>();
        services.AddSingleton<IPowerService, Interop.Win32.Power.PowerService>();
        services.AddSingleton<IRestorePointService, Interop.Win32.Restore.RestorePointService>();
        services.AddSingleton<IShellExtensionService, ShellExtensionService>();
        services.AddSingleton<IContextMenuProbe, ContextMenuProbe>();
        services.AddSingleton(new Modules.Startup.Services.TaskClassificationOverrideStore(
            Path.Combine(Core.AppConstants.DataDirectoryPath, "task-classifications.txt")));

        services.AddSingleton<IModule, ShellModule>();
        services.AddSingleton<IModule, ContextMenuModule>();
        services.AddSingleton<IModule, EnvironmentModule>();
        services.AddSingleton<IModule, StartupModule>();
        services.AddSingleton<IModule, AnnoyancesModule>();
        services.AddSingleton<IModule, PrivacyModule>();
        services.AddSingleton<IModule, WindowsUpdateModule>();
        services.AddSingleton<IModule, Modules.Software.SoftwareModule>();
        services.AddSingleton<IModule, PowerModule>();
        services.AddSingleton<IEnforcementExecutor, EnforcementExecutor>();

        _services = services.BuildServiceProvider();
        _modules = _services.GetServices<IModule>()
            .ToDictionary(module => module.Info.Name, StringComparer.Ordinal);
        _executor = new(_services.GetRequiredService<IEnforcementExecutor>());

        _restoration = new(_services.GetRequiredService<IRegistryService>(), primaryUserSid: uiUserSid);
        _deliberateChanges = new(
            _restoration.Coordinator,
            _restoration.Baseline,
            _services.GetRequiredService<IRegistryService>(),
            TimeProvider.System);
    }

    internal Task<OperationResult<bool>> Apply(
        ChangeDescriptor change, bool revert, CancellationToken cancellationToken)
    {
        if (!_modules.TryGetValue(change.ModuleId, out var module))
        {
            return Task.FromResult(OperationResult<bool>.Failure(
                $"Module '{change.ModuleId}' is not available in the privilege broker.",
                ErrorCategory.NotFound));
        }

        if (!CanApplyProtectedChoice(change, _uiUserSid, _brokerUserSid))
            return Task.FromResult(OperationResult<bool>.Failure(
                "Protected settings require administrator approval from the same Windows account that opened ThisIsMyPC.",
                ErrorCategory.AccessDenied));

        var applied = revert ? Invert(change) : change;
        return _deliberateChanges.RunAsync(async (session, token) =>
        {
            token.ThrowIfCancellationRequested();
            session.Prepare([applied]);
            var result = revert
                ? await _executor.RevertAsync(change, module.RevertChangeAsync).ConfigureAwait(false)
                : await _executor.ApplyAsync(change, module.ApplyChangeAsync).ConfigureAwait(false);
            if (!result.IsSuccess) return result;
            return await SaveProtectedChoiceAsync(
                () => session.RecordApplied([applied]),
                () => session.DisableProtection(_restoration.Consent),
                () => revert
                    ? _executor.ApplyAsync(change, module.ApplyChangeAsync)
                    : _executor.RevertAsync(change, module.RevertChangeAsync)).ConfigureAwait(false);
        }, cancellationToken);
    }

    internal static bool CanApplyProtectedChoice(ChangeDescriptor change, string uiUserSid, string? brokerUserSid)
    {
        var location = change.SystemLocation.Replace("HKEY_CURRENT_USER\\", "HKCU\\", StringComparison.OrdinalIgnoreCase);
        var catalogTarget = RestorationCatalog.Default.Targets.Any(target =>
            string.Equals(location, target.KeyPath + "\\" + target.ValueName, StringComparison.OrdinalIgnoreCase));
        return !catalogTarget || string.Equals(uiUserSid, brokerUserSid, StringComparison.Ordinal);
    }

    internal static async Task<OperationResult<bool>> SaveProtectedChoiceAsync(Action save, Action disableProtection,
        Func<Task<OperationResult<bool>>> rollback)
    {
        try
        {
            save();
            return OperationResult<bool>.Success(true);
        }
        catch (Exception saveError)
        {
            try
            {
                disableProtection();
            }
            catch (Exception protectionError)
            {
                return OperationResult<bool>.Failure(
                    "The setting changed, but its protected choice could not be secured. Rollback was not attempted: " + protectionError.Message,
                    ErrorCategory.ServiceUnavailable);
            }
            try
            {
                var restored = await rollback().ConfigureAwait(false);
                if (restored.IsSuccess)
                    return OperationResult<bool>.Failure(
                        "The protected choice could not be saved. The setting was restored to its previous value: " + saveError.Message,
                        ErrorCategory.ServiceUnavailable);
                return OperationResult<bool>.Failure(
                    "The setting changed, but protection could not be saved and rollback failed. Its state is uncertain: " + restored.ErrorMessage,
                    ErrorCategory.ServiceUnavailable);
            }
            catch (Exception rollbackError)
            {
                return OperationResult<bool>.Failure(
                    "The setting changed, but protection could not be saved and rollback failed. Its state is uncertain: " + rollbackError.Message,
                    ErrorCategory.ServiceUnavailable);
            }
        }
    }

    internal Task<OperationResult<bool>> Execute(ActionDescriptor action)
    {
        if (!_modules.TryGetValue(action.ModuleId, out var module) || module is not IActionModule actionModule)
        {
            return Task.FromResult(OperationResult<bool>.Failure(
                $"Module '{action.ModuleId}' cannot execute broker actions.",
                ErrorCategory.NotFound));
        }

        return actionModule.ExecuteActionAsync(action);
    }

    internal Task<RestorePointResult> CreateRestorePoint(string description) =>
        _services.GetRequiredService<IRestorePointService>().CreateRestorePointAsync(description);

    public async ValueTask DisposeAsync()
    {
        _deliberateChanges.Dispose();
        _restoration.Dispose();
        foreach (var module in _modules.Values)
            await module.DisposeAsync().ConfigureAwait(false);
        await _services.DisposeAsync().ConfigureAwait(false);
    }

    private static ChangeDescriptor Invert(ChangeDescriptor change) => change with
    {
        BeforeValue = change.AfterValue ?? string.Empty,
        AfterValue = change.BeforeValue,
        BeforeDisplay = change.AfterDisplay ?? string.Empty,
        AfterDisplay = change.BeforeDisplay,
    };
}
