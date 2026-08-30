using Avalonia;
using Serilog;
using Serilog.Events;
using Serilog.Formatting.Compact;
using System;
using System.IO;
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
        VelopackApp.Build()
            .SetAutoApplyOnStartup(false)
            .Run();

        var dataDir = AppConstants.DataDirectoryPath;
        Directory.CreateDirectory(dataDir);

        var logPath = Path.Combine(dataDir, "logs", "thisismypc-.log");

        Log.Logger = new LoggerConfiguration()
#if DEBUG
            .MinimumLevel.Verbose()
#else
            .MinimumLevel.Information()
            .MinimumLevel.Override("ThisIsMyPC.Interop", LogEventLevel.Warning)
#endif
            .WriteTo.File(
                new CompactJsonFormatter(),
                logPath,
                rollingInterval: RollingInterval.Day,
                retainedFileCountLimit: 7,
                fileSizeLimitBytes: 10 * 1024 * 1024)
            .CreateLogger();

#pragma warning disable CA1031 // Top-level crash handler must catch all exceptions
        try
        {
            Log.Information("ThisIsMyPC starting");

            var installGuard = new InstallationGuard(AppContext.BaseDirectory);
            InstallGuard = installGuard;
            if (installGuard.IsProtectedLocation)
                Log.Information("Installation path verified: {Path}", AppContext.BaseDirectory);
            else
                Log.Warning("Unprotected install location: {Path}: {Warning}",
                    AppContext.BaseDirectory, installGuard.WarningMessage);

            var guard = new DataDirectoryGuard();
            var daclResult = guard.EnsureHardened(dataDir);
            if (daclResult.IsSuccess)
                Log.Information("Data directory DACL: {Status}", daclResult.Value);
            else
                Log.Warning("Data directory DACL hardening failed: {Error}", daclResult.ErrorMessage);

            BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
        }
        catch (Exception ex)
        {
            Log.Fatal(ex, "Application terminated unexpectedly");
        }
#pragma warning restore CA1031
        finally
        {
            Log.Information("ThisIsMyPC shutting down");
            Log.CloseAndFlush();
        }
    }

    public static AppBuilder BuildAvaloniaApp()
        => AppBuilder.Configure<App>()
            .UsePlatformDetect()
            .LogToTrace();
}
