using Avalonia;
using NLog;
using System;
using ThisIsMyPC.App.Services;
using ThisIsMyPC.Core;
using ThisIsMyPC.Core.Services;
using ThisIsMyPC.Interop.Win32;
using Velopack;

namespace ThisIsMyPC.App;

sealed class Program
{
    internal static IInstallationGuard? InstallGuard { get; private set; }

    [STAThread]
    public static void Main(string[] args)
    {
        // First: drop working directory and PATH from every DLL resolution in
        // the process (System32 + application dir only). Must precede any code
        // that could fault in a library.
        Interop.Win32.Security.DllSearchHardening.Apply();

#if DEBUG
        // Debug builds get a separate console window streaming verbose logs.
        // Must run before anything touches System.Console (handles are cached).
        var hasDebugConsole = DebugConsole.Attach();
        if (hasDebugConsole)
            Console.Title = "ThisIsMyPC logs (Debug)";
#endif

        VelopackApp.Build()
            .SetAutoApplyOnStartup(false)
            .Run();

        var dataDir = AppConstants.DataDirectoryPath;
        Directory.CreateDirectory(dataDir);

#if DEBUG
        var log = LoggingSetup.Configure(dataDir, verbose: true, console: hasDebugConsole);
#else
        var log = LoggingSetup.Configure(dataDir, verbose: false, console: false);
#endif

#pragma warning disable CA1031 // Top-level crash handler must catch all exceptions
        try
        {
            log.Info("ThisIsMyPC starting");

            var installGuard = new InstallationGuard(AppContext.BaseDirectory);
            InstallGuard = installGuard;
            if (installGuard.IsProtectedLocation)
                log.Info("Installation path verified: {Path}", AppContext.BaseDirectory);
            else
                log.Warn("Unprotected install location: {Path}: {Warning}",
                    AppContext.BaseDirectory, installGuard.WarningMessage);

            var guard = new DataDirectoryGuard();
            var daclResult = guard.EnsureHardened(dataDir);
            if (daclResult.IsSuccess)
                log.Info("Data directory DACL: {Status}", daclResult.Value);
            else
                log.Warn("Data directory DACL hardening failed: {Error}", daclResult.ErrorMessage);

            // Pre-machine-scope builds stored data in %APPDATA%; bring it along
            // once, after hardening and before any service opens the files.
            LegacyDataMigration.CopyFromUserProfile(dataDir, log);

            BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
        }
        catch (Exception ex)
        {
            log.Fatal(ex, "Application terminated unexpectedly");
        }
#pragma warning restore CA1031
        finally
        {
            log.Info("ThisIsMyPC shutting down");
            LogManager.Shutdown();
        }
    }

    public static AppBuilder BuildAvaloniaApp()
        => AppBuilder.Configure<App>()
            .UsePlatformDetect()
            .LogToTrace();
}
