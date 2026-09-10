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
    private static readonly NLog.Logger Log = NLog.LogManager.GetLogger("ThisIsMyPC.App");

    private ServiceProvider? _serviceProvider;
    private WindowPersistenceController? _windowController;
    private TrayService? _trayService;
    private AutoStartService? _autoStartService;
    private AccessibilityFontService? _fontService;
    private bool _shutdownStarted;
    private bool _shutdownReady;

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
            InitializeSettings(_serviceProvider.GetRequiredService<Core.Settings.ISettingsService>());

            // Saved theme before MainWindow exists so the first frame is already themed.
            Services.ThemeService.Apply(_serviceProvider
                .GetRequiredService<Core.Settings.ISettingsService>()
                .GetApp(Core.Settings.AppSettingKeys.Theme, Services.ThemeService.Dark));

            // 10-4: live OpenDyslexic body-font override (before MainWindow exists so
            // the first render already uses the preferred font)
            _fontService = new AccessibilityFontService(
                _serviceProvider.GetRequiredService<Core.Settings.ISettingsService>(), Resources);

            var mainViewModel = _serviceProvider.GetRequiredService<MainWindowViewModel>();
            desktop.MainWindow = new MainWindow
            {
                DataContext = mainViewModel,
            };

            // 9-1: tray mode + window behavior (opt-in; defaults are stock Windows)
            var settingsService = _serviceProvider.GetRequiredService<Core.Settings.ISettingsService>();
            var pendingChanges = _serviceProvider.GetRequiredService<IPendingChangesService>();
            _trayService = new TrayService(
                settingsService,
                pendingChanges,
                openWindow: () => _windowController!.ShowWindow(),
                applyPending: () =>
                {
                    _windowController!.ShowWindow();
                    if (mainViewModel.ApplyAllCommand.CanExecute(null))
                        mainViewModel.ApplyAllCommand.Execute(null);
                },
                exit: () => _windowController!.RequestExit());
            // Hide-to-tray must never engage when the tray icon failed to materialize;
            // a hidden window with no tray would be unreachable.
            _windowController = new WindowPersistenceController(
                desktop.MainWindow, desktop, settingsService,
                trayAvailable: () => _trayService!.IsTrayActive);

            // 9-3: opt-in monitoring loop (runs only while the app is in memory)
            _serviceProvider.GetRequiredService<Core.Monitoring.MonitoringService>().Start();

            // The machine data directory (drift baseline included) is created and
            // DACL-hardened once in Program.Main, before anything reads it.

            // 28-3: one drift-report fetch; silently a no-op when the service is off
            _ = mainViewModel.LoadDriftReportAsync();

            // 9-2: auto-start reconcile + minimized launch
            _autoStartService = new AutoStartService(
                _serviceProvider.GetRequiredService<IRegistryService>(), settingsService);
            _autoStartService.Reconcile();

            if (desktop.Args?.Contains("--minimized", StringComparer.Ordinal) == true)
            {
                if (settingsService.GetAppBool(Core.Settings.AppSettingKeys.TrayMode, false))
                {
                    // One-shot: Opened fires on EVERY Show(), so a persistent handler
                    // would re-hide the window each time the user opens it from the tray.
                    EventHandler? hideOnce = null;
                    hideOnce = (_, _) =>
                    {
                        desktop.MainWindow.Opened -= hideOnce;
                        desktop.MainWindow.Hide();
                    };
                    desktop.MainWindow.Opened += hideOnce;
                }
                else
                {
                    desktop.MainWindow.WindowState = Avalonia.Controls.WindowState.Minimized;
                }
            }

            // Run after the tray close guard. A normal close must wait for active mutations too.
            desktop.MainWindow.Closing += (_, e) =>
            {
                if (e.Cancel || _shutdownReady) return;
                e.Cancel = true;
                desktop.TryShutdown();
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
        Log.Info("Set discovery: {Count} set(s) loaded", load.Sets.Count);
        foreach (var warning in load.Warnings)
            Log.Warn("Set discovery: {Warning}", warning);
    }

    private static void InitializeSettings(Core.Settings.ISettingsService settingsService)
    {
        settingsService.Initialize();
        InstallerPreferenceImport.Apply(settingsService);
        WindowBehaviorPolicy.NormalizeLegacySettings(settingsService);
        if (settingsService.LoadError is { } error)
            Log.Warn("Settings load: {Error}", error);
        if (settingsService.SettingsWereReset)
            Log.Warn("Settings were reset to defaults; previous file preserved as settings.json.bad");
    }

    /// <summary>
    /// Whether this PC has a built-in display panel, read once: the Display
    /// module's laptop-panel path walks every setting of the active power
    /// plan, and the answer cannot change while the app runs. No battery
    /// means no panel; a battery with no readable brightness setting is
    /// unknown, never "no panel".
    /// </summary>
    private static Func<bool?> InternalPanelProbe(IServiceProvider sp)
    {
        var panel = new Lazy<bool?>(() =>
        {
            if (!sp.GetRequiredService<IMonitorService>().HasSystemBattery())
                return false;
            var read = new ThisIsMyPC.Modules.Display.Services.InternalPanelService(sp.GetRequiredService<IPowerService>()).ReadPanel();
            return read is not null ? true : null;
        }, LazyThreadSafetyMode.ExecutionAndPublication);
        return () => panel.Value;
    }

    // Internal so the headless UI test harness can build the real service graph
    // and swap in test-safe substitutes (winget, restore points, data paths).
    internal static void ConfigureServices(IServiceCollection services)
    {
        // Installation guard (pre-created in Program.Main)
        if (Program.InstallGuard is not null)
            services.AddSingleton<IInstallationGuard>(Program.InstallGuard);

        // Interop services
        services.AddSingleton<ISecurityApi, SecurityApi>();
        services.AddSingleton<IRegistryService, RegistryService>();
        services.AddSingleton<Core.Hardware.IHardwareDetectionService, Interop.Win32.Hardware.HardwareDetectionService>();
        services.AddSingleton(_ => new Services.AutorunEnrichment());
        services.AddSingleton<IShellExtensionService, ShellExtensionService>();
        services.AddSingleton<IContextMenuProbe, ContextMenuProbe>();
        services.AddSingleton<IInteractiveUserContext, DesktopUserContext>();
        services.AddSingleton<IExplorerRestartService, ExplorerRestartService>();
        services.AddSingleton<IEnvironmentBroadcaster, EnvironmentBroadcaster>();
        services.AddSingleton<IServiceControlService, ServiceControlService>();
        services.AddSingleton<IStartupFolderService, StartupFolderService>();
        services.AddSingleton<IScheduledTaskService, ScheduledTaskService>();
        services.AddSingleton(new ThisIsMyPC.Modules.Startup.Services.TaskClassificationOverrideStore(
            System.IO.Path.Combine(AppConstants.UserDataDirectoryPath, "task-classifications.txt")));
        services.AddSingleton<IAppxPackageService, AppxPackageService>();
        services.AddSingleton<IWingetService, ThisIsMyPC.Interop.Win32.Packages.WingetService>();
        services.AddSingleton<IPowerService, ThisIsMyPC.Interop.Win32.Power.PowerService>();
        services.AddSingleton<IMonitorService, ThisIsMyPC.Interop.Win32.Display.DdcMonitorService>();
        services.AddSingleton<IRestorePointService, ThisIsMyPC.Interop.Win32.Restore.RestorePointService>();

        // Modules (explicit DI registration, NativeAOT-safe)
        services.AddSingleton<IModule, ShellModule>();
        services.AddSingleton<IModule, ContextMenuModule>();
        services.AddSingleton<IModule, EnvironmentModule>();
        services.AddSingleton<IModule, StartupModule>();
        services.AddSingleton<IModule, ThisIsMyPC.Modules.Annoyances.AnnoyancesModule>();
        services.AddSingleton<IModule, ThisIsMyPC.Modules.Privacy.PrivacyModule>();
        services.AddSingleton<IModule, ThisIsMyPC.Modules.WindowsUpdate.WindowsUpdateModule>();
        services.AddSingleton<IModule, ThisIsMyPC.Modules.Software.SoftwareModule>();
        services.AddSingleton<IModule, PowerModule>();
        services.AddSingleton<IModule, ThisIsMyPC.Modules.Display.DisplayModule>();

        // Lighting: the built-in controllers (docs/lighting-controllers.md)
        // over HID (hid.dll) and GPU I2C (NvAPI, mapped before CIG by
        // Program). One backend for the life of the app: detection runs once
        // per pass and the page's sessions share the controllers it found.
        services.AddSingleton<Core.Hardware.Lighting.ILightingBackend>(_ => new ThisIsMyPC.Lighting.NativeLightingBackend(
            new ThisIsMyPC.Interop.Win32.Hardware.Hid.WindowsHidTransport(),
            new ThisIsMyPC.Interop.Win32.Hardware.I2c.NvApiI2cBusProvider()));
        // Hardware tabs (v1 plan section 5): the module-level facts layer over
        // the shared inventory. It adds what the tabs need and the inventory
        // leaves unobserved: companion launch paths and ownership, the OpenRGB
        // SDK probe, the lighting device list, the internal panel. Every probe is a read.
        services.AddSingleton<Core.Hardware.Detection.IHardwareProbeEnvironment, ThisIsMyPC.Interop.Win32.Hardware.Win32HardwareProbeEnvironment>();
        services.AddSingleton<Core.Hardware.IHardwareFactsProvider>(sp => new Core.Hardware.Detection.HardwareFactsProvider(
            sp.GetRequiredService<Core.Hardware.IHardwareDetectionService>(),
            sp.GetRequiredService<IRegistryService>(),
            sp.GetRequiredService<Core.Hardware.Detection.IHardwareProbeEnvironment>(),
            sp.GetRequiredService<IScheduledTaskService>(),
            sp.GetRequiredService<IServiceControlService>(),
            internalPanelProbe: InternalPanelProbe(sp),
            lighting: sp.GetRequiredService<Core.Hardware.Lighting.ILightingBackend>()));
        services.AddSingleton<ICompanionWindowService, ThisIsMyPC.Interop.Win32.Hardware.CompanionWindowService>();
        services.AddSingleton(sp => new HardwareCompanionActions(
            sp.GetRequiredService<IPendingActionsService>(),
            sp.GetRequiredService<IInteractiveUserContext>(),
            sp.GetRequiredService<ICompanionWindowService>()));
        services.AddSingleton<IModule, ThisIsMyPC.Modules.Hardware.SystemControlModule>();
        services.AddSingleton<IModule, ThisIsMyPC.Modules.Hardware.LightingModule>();
        services.AddSingleton<IModule, ThisIsMyPC.Modules.Hardware.CoolingModule>();
        services.AddSingleton<IModule, ThisIsMyPC.Modules.Hardware.MonitoringModule>();

        // Update services. GPG manifest verification (tm2:54): fail-closed,
        // offline release key, public key hardcoded in the verifier.
        services.AddSingleton<IUpdateVerifier, GpgManifestUpdateVerifier>();
        services.AddSingleton<IUpdateService>(sp =>
            new VelopackUpdateService(
                AppConstants.UpdateUrl,
                sp.GetService<IUpdateVerifier>()));

        // Owner Mode IPC client (28-1); connects per request; a missing service
        // degrades to ServiceUnavailable, never an error dialog.
        services.AddSingleton<ThisIsMyPC.Ipc.Contracts.IIpcClient>(_ => new ThisIsMyPC.Ipc.Contracts.IpcClient());
        services.AddSingleton<IPrivilegeBrokerClient, PrivilegeBrokerClient>();

        // Owner Mode lifecycle (28-2): SCM registration + live capability probe.
        services.AddSingleton<IServiceInstaller, ServiceInstaller>();
        services.AddSingleton(sp => new OwnerModeService(
            sp.GetRequiredService<IServiceInstaller>(),
            sp.GetRequiredService<IServiceControlService>(),
            ipc: sp.GetRequiredService<ThisIsMyPC.Ipc.Contracts.IIpcClient>(),
            privilegeBroker: sp.GetRequiredService<IPrivilegeBrokerClient>()));

        // Core Services. The capability detector is wrapped by the Debug page's
        // simulation seam: a pass-through until the Debug page (Debug builds only)
        // sets an override, held in memory only, never persisted.
        services.AddSingleton<Services.DebugSimulation>();
        services.AddSingleton<ICapabilityDetector>(sp => new Services.SimulatedCapabilityDetector(
            new CapabilityDetector(
                sp.GetRequiredService<IRegistryService>(),
                ownerModeProbe: () => sp.GetRequiredService<OwnerModeService>().IsRestorationEnabled),
            sp.GetRequiredService<Services.DebugSimulation>()));
        // PendingChangesService's optional ctor param resolves this because it is registered.
        services.AddSingleton<IEnforcementExecutor, BrokerRoutingEnforcementExecutor>();
        services.AddSingleton<IPendingChangesService, PendingChangesService>();
        services.AddSingleton<IPendingActionsService, PendingActionsService>();
        services.AddSingleton<ISetProvider>(_ => new SetProvider(
            Path.Combine(AppContext.BaseDirectory, "sets"),
            Path.Combine(AppConstants.UserDataDirectoryPath, "sets")));
        // Custom set creation (8.5) writes into the same user sets directory.
        services.AddSingleton<ICustomSetWriter>(_ => new CustomSetWriter(
            Path.Combine(AppConstants.UserDataDirectoryPath, "sets")));
        // Per-tab display-mode persistence (10.2).
        services.AddSingleton(_ => new DisplayModePreferencesStore(
            Path.Combine(AppConstants.UserDataDirectoryPath, "display-modes.txt")));
        // Per-module set-entry inspectors for the Set Loader preview (8.2) and
        // conflict detection (8.3)
        services.AddSingleton<ISetEntryInspector, ThisIsMyPC.Modules.Shell.Services.ShellSetEntryInspector>();
        services.AddSingleton<ISetEntryInspector, ThisIsMyPC.Modules.Annoyances.Services.AnnoyancesSetEntryInspector>();
        services.AddSingleton<ISetEntryInspector, ThisIsMyPC.Modules.Privacy.Services.PrivacySetEntryInspector>();
        services.AddSingleton<ISetEntryInspector, ThisIsMyPC.Modules.Startup.Services.StartupSetEntryInspector>();
        services.AddSingleton<ISetEntryInspector, ThisIsMyPC.Modules.WindowsUpdate.Services.WindowsUpdateSetEntryInspector>();

        // Cross-module search contributors (5-3)
        services.AddSingleton<Core.Search.ISearchSettingsContributor, ThisIsMyPC.Modules.Annoyances.Services.AnnoyancesSearchContributor>();
        services.AddSingleton<Core.Search.ISearchSettingsContributor, ThisIsMyPC.Modules.Privacy.Services.PrivacySearchContributor>();
        services.AddSingleton<Core.Search.ISearchSettingsContributor, ThisIsMyPC.Modules.WindowsUpdate.Services.WindowsUpdateSearchContributor>();
        services.AddSingleton<Core.Search.ISearchSettingsContributor, ThisIsMyPC.Modules.Shell.Services.ExplorerSearchContributor>();
        services.AddSingleton<Core.Search.ISearchSettingsContributor, ThisIsMyPC.Modules.Shell.Services.ContextMenuSearchContributor>();
        services.AddSingleton<Core.Search.ISearchSettingsContributor, ThisIsMyPC.Modules.Shell.Services.EnvironmentSearchContributor>();
        services.AddSingleton<Core.Search.ISearchSettingsContributor, ThisIsMyPC.Modules.Startup.Services.StartupSearchContributor>();
        services.AddSingleton<Core.Search.ISearchSettingsContributor, ThisIsMyPC.Modules.Power.Services.PowerSearchContributor>();
        services.AddSingleton<Core.Settings.ISettingsService>(_ => new Core.Settings.SettingsService(
            Path.Combine(AppConstants.UserDataDirectoryPath, "settings.json")));
        services.AddSingleton<Core.Notifications.INotificationService, Core.Notifications.NotificationService>();
        services.AddSingleton<Core.Monitoring.IMonitoringSnapshotProvider, MonitoringSnapshotProvider>();
        services.AddSingleton(sp => new Core.Monitoring.MonitoringService(
            sp.GetRequiredService<Core.Settings.ISettingsService>(),
            sp.GetRequiredService<Core.Notifications.INotificationService>(),
            sp.GetRequiredService<Core.Monitoring.IMonitoringSnapshotProvider>(),
            Path.Combine(AppConstants.UserDataDirectoryPath, "monitoring.json")));
        services.AddSingleton<ChangeHistoryRepository>();
        services.AddSingleton<IChangeHistoryService>(sp => new ChangeHistoryService(
            sp.GetRequiredService<ChangeHistoryRepository>(),
            Path.Combine(AppConstants.UserDataDirectoryPath, "history.db"),
            sp.GetRequiredService<IEnforcementExecutor>()));

        // Navigation
        services.AddSingleton<NavigationService>();

        // ViewModels
        services.AddSingleton<MainWindowViewModel>();
        services.AddSingleton<ReviewPanelViewModel>();
    }

    private async void OnShutdownRequested(object? sender, ShutdownRequestedEventArgs e)
    {
        if (_shutdownReady) return;
        e.Cancel = true;
        if (_shutdownStarted) return;
        _shutdownStarted = true;
        try
        {
            if (_serviceProvider is not null)
                await _serviceProvider.GetRequiredService<MainWindowViewModel>().PrepareForShutdownAsync().ConfigureAwait(true);
            _trayService?.Dispose();
            _trayService = null;
            _autoStartService?.Dispose();
            _autoStartService = null;
            _fontService?.Dispose();
            _fontService = null;
            _windowController?.Dispose();
            _windowController = null;
            if (_serviceProvider is not null)
            {
                await _serviceProvider.DisposeAsync().ConfigureAwait(true);
                _serviceProvider = null;
            }
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Shutdown cleanup failed after waiting for active changes");
        }
        finally
        {
            _shutdownReady = true;
            // Post after the cancelled request returns, avoiding recursive lifetime shutdown.
            if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
                Avalonia.Threading.Dispatcher.UIThread.Post(() => desktop.Shutdown());
        }
    }

    [UnconditionalSuppressMessage("Trimming", "IL2026",
        Justification = "DataValidators access is safe; Avalonia initializes these before this runs")]
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
