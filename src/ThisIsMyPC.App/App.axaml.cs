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
using ThisIsMyPC.Core.Modules;
using ThisIsMyPC.Core.Data;
using ThisIsMyPC.Core.Services;
using ThisIsMyPC.Interop.Win32.Registry;
using ThisIsMyPC.Interop.Com.Shell;
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

    private static void ConfigureServices(IServiceCollection services)
    {
        // Interop services
        services.AddSingleton<IRegistryService, RegistryService>();
        services.AddSingleton<IShellExtensionService, ShellExtensionService>();

        // Modules (explicit DI registration, NativeAOT-safe)
        services.AddSingleton<IModule, ShellModule>();
        services.AddSingleton<IModule, ContextMenuModule>();
        services.AddSingleton<IModule, StartupModule>();
        services.AddSingleton<IModule, PowerModule>();

        // Core Services
        services.AddSingleton<IPendingChangesService, PendingChangesService>();
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
