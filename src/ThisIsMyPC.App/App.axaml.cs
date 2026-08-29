using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Data.Core.Plugins;
using Avalonia.Markup.Xaml;
using Microsoft.Extensions.DependencyInjection;
using System;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using ThisIsMyPC.App.Services;
using ThisIsMyPC.App.ViewModels;
using ThisIsMyPC.App.Views;
using ThisIsMyPC.Core;
using ThisIsMyPC.Core.Modules;
using ThisIsMyPC.Core.Packages;
using ThisIsMyPC.Core.Data;
using ThisIsMyPC.Core.Enforcement;
using ThisIsMyPC.Core.Services;
using ThisIsMyPC.Core.Sets;
using ThisIsMyPC.Interop.Win32;
using ThisIsMyPC.Interop.Win32.Registry;
using ThisIsMyPC.Interop.Win32.Security;
using ThisIsMyPC.Interop.Win32.Services;
using ThisIsMyPC.Interop.Com.Packages;
using ThisIsMyPC.Interop.Com.Shell;
using ThisIsMyPC.Interop.Com.Startup;
using ThisIsMyPC.Interop.Com.Tasks;
using ThisIsMyPC.Modules.Power;
using ThisIsMyPC.Modules.Shell;
using ThisIsMyPC.Modules.Startup;

namespace ThisIsMyPC.App;

public partial class App : Application
{
    private ServiceProvider? _serviceProvider;

    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);
    }

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            DisableAvaloniaDataAnnotationValidation();

            var services = new ServiceCollection();
            ConfigureServices(services);
            _serviceProvider = services.BuildServiceProvider();

            LogSetDiscovery(_serviceProvider.GetRequiredService<ISetProvider>());

            desktop.MainWindow = new MainWindow
            {
                DataContext = _serviceProvider.GetRequiredService<MainWindowViewModel>(),
            };

            desktop.ShutdownRequested += OnShutdownRequested;
        }

        base.OnFrameworkInitializationCompleted();

#if DEBUG
        this.AttachDevTools();
#endif
    }

    private static void LogSetDiscovery(ISetProvider setProvider)
    {
        // Until Story 8.4 bundles built-in sets, the missing built-in directory warning
        // is expected on every install.
        var load = setProvider.LoadSets();
        Serilog.Log.Information("Set discovery: {Count} set(s) loaded", load.Sets.Count);
        foreach (var warning in load.Warnings)
            Serilog.Log.Warning("Set discovery: {Warning}", warning);
    }

    private static void ConfigureServices(IServiceCollection services)
    {
        // Installation guard (pre-created in Program.Main)
        if (Program.InstallGuard is not null)
            services.AddSingleton<IInstallationGuard>(Program.InstallGuard);

        // Interop services
        services.AddSingleton<ISecurityApi, SecurityApi>();
        services.AddSingleton<IDataDirectoryGuard, DataDirectoryGuard>();
        services.AddSingleton<IRegistryService, RegistryService>();
        services.AddSingleton<IShellExtensionService, ShellExtensionService>();
        services.AddSingleton<IContextMenuProbe, ContextMenuProbe>();
        services.AddSingleton<IExplorerRestartService, ExplorerRestartService>();
        services.AddSingleton<IEnvironmentBroadcaster, EnvironmentBroadcaster>();
        services.AddSingleton<IServiceControlService, ServiceControlService>();
        services.AddSingleton<IStartupFolderService, StartupFolderService>();
        services.AddSingleton<IScheduledTaskService, ScheduledTaskService>();
        services.AddSingleton(new ThisIsMyPC.Modules.Startup.Services.TaskClassificationOverrideStore(
            System.IO.Path.Combine(AppConstants.DataDirectoryPath, "task-classifications.txt")));
        services.AddSingleton<IAppxPackageService, AppxPackageService>();
        services.AddSingleton<IPowerService, ThisIsMyPC.Interop.Win32.Power.PowerService>();

        // Modules (explicit DI registration, NativeAOT-safe)
        services.AddSingleton<IModule, ShellModule>();
        services.AddSingleton<IModule, ContextMenuModule>();
        services.AddSingleton<IModule, EnvironmentModule>();
        services.AddSingleton<IModule, StartupModule>();
        services.AddSingleton<IModule, ThisIsMyPC.Modules.Annoyances.AnnoyancesModule>();
        services.AddSingleton<IModule, PowerModule>();

        // Update services
        services.AddSingleton<IUpdateVerifier, AuthenticodeUpdateVerifier>();
        services.AddSingleton<IUpdateService>(sp =>
            new VelopackUpdateService(
                AppConstants.UpdateUrl,
                sp.GetService<IUpdateVerifier>()));

        // Core Services
        services.AddSingleton<ICapabilityDetector, CapabilityDetector>();
        // PendingChangesService's optional ctor param resolves this because it is registered.
        services.AddSingleton<IEnforcementExecutor, EnforcementExecutor>();
        services.AddSingleton<IPendingChangesService, PendingChangesService>();
        services.AddSingleton<ISetProvider>(_ => new SetProvider(
            Path.Combine(AppContext.BaseDirectory, "sets"),
            Path.Combine(AppConstants.DataDirectoryPath, "sets")));
        // Custom set creation (8.5) writes into the same user sets directory.
        services.AddSingleton<ICustomSetWriter>(_ => new CustomSetWriter(
            Path.Combine(AppConstants.DataDirectoryPath, "sets")));
        // Per-tab display-mode persistence (10.2).
        services.AddSingleton(_ => new DisplayModePreferencesStore(
            Path.Combine(AppConstants.DataDirectoryPath, "display-modes.txt")));
        // Per-module set-entry inspectors for the Set Loader preview (8.2) and
        // conflict detection (8.3)
        services.AddSingleton<ISetEntryInspector, ThisIsMyPC.Modules.Shell.Services.ShellSetEntryInspector>();
        services.AddSingleton<ISetEntryInspector, ThisIsMyPC.Modules.Annoyances.Services.AnnoyancesSetEntryInspector>();
        services.AddSingleton<ISetEntryInspector, ThisIsMyPC.Modules.Startup.Services.StartupSetEntryInspector>();
        services.AddSingleton<ChangeHistoryRepository>();
        services.AddSingleton<IChangeHistoryService, ChangeHistoryService>();

        // Navigation
        services.AddSingleton<NavigationService>();

        // ViewModels
        services.AddSingleton<MainWindowViewModel>();
        services.AddSingleton<ReviewPanelViewModel>();
    }

    private async void OnShutdownRequested(object? sender, ShutdownRequestedEventArgs e)
    {
        try
        {
            if (_serviceProvider is not null)
            {
                await _serviceProvider.DisposeAsync().ConfigureAwait(false);
                _serviceProvider = null;
            }
        }
        catch (Exception)
        {
            // Swallow shutdown cleanup failures to prevent crash during exit
        }
    }

    [UnconditionalSuppressMessage("Trimming", "IL2026",
        Justification = "DataValidators access is safe — Avalonia initializes these before this runs")]
    private static void DisableAvaloniaDataAnnotationValidation()
    {
        var dataValidationPluginsToRemove =
            BindingPlugins.DataValidators.OfType<DataAnnotationsValidationPlugin>().ToArray();

        foreach (var plugin in dataValidationPluginsToRemove)
        {
            BindingPlugins.DataValidators.Remove(plugin);
        }
    }
}
