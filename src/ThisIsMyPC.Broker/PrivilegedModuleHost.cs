using Microsoft.Extensions.DependencyInjection;
using ThisIsMyPC.App.Services;
using ThisIsMyPC.Core.Actions;
using ThisIsMyPC.Core.Changes;
using ThisIsMyPC.Core.Coordination;
using ThisIsMyPC.Core.Drift;
using ThisIsMyPC.Core.Drift.Baseline;
using ThisIsMyPC.Core.Drift.Consent;
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
using ThisIsMyPC.Interop.Win32.Coordination;
using ThisIsMyPC.Interop.Win32.Drift.Baseline;
using ThisIsMyPC.Interop.Win32.Drift.Consent;
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

    internal PrivilegedModuleHost(string uiUserSid)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(uiUserSid);
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

        var leaseProvider = new NamedMutexMutationLeaseProvider(
            MutationLeaseNames.Production, "privilege-broker");
        var consent = new MachineConsentStore(leaseName: leaseProvider.Name);
        var mutationCoordinator = new MutationCoordinator(leaseProvider, (lease, token) =>
        {
            token.ThrowIfCancellationRequested();
            var off = consent.SetEnabled(false, lease);
            return Task.FromResult(off.IsSuccess
                && off.State.Status == MachineConsentStatus.Loaded
                && !off.State.Enabled
                    ? OperationResult<bool>.Success(true)
                    : OperationResult<bool>.Failure(
                        "Could not pause Owner Mode before the change: " + off.State.Detail,
                        ErrorCategory.ServiceUnavailable));
        });
        _deliberateChanges = new(
            mutationCoordinator,
            new SingleOwnerBaselineStore(
                new MachineBaselineStorage(), leaseProvider.Name, uiUserSid),
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

        var applied = revert ? Invert(change) : change;
        return _deliberateChanges.RunAsync(async (session, token) =>
        {
            token.ThrowIfCancellationRequested();
            session.Prepare([applied]);
            var result = revert
                ? await _executor.RevertAsync(change, module.RevertChangeAsync).ConfigureAwait(false)
                : await _executor.ApplyAsync(change, module.ApplyChangeAsync).ConfigureAwait(false);
            if (result.IsSuccess)
                session.RecordApplied([applied]);
            return result;
        }, cancellationToken);
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
