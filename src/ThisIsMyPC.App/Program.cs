using Avalonia;
using NLog;
using System;
using ThisIsMyPC.App.Services;
using ThisIsMyPC.Core;
using ThisIsMyPC.Core.Services;
using ThisIsMyPC.Interop.Win32;
using ThisIsMyPC.Interop.Win32.Security;
using Velopack;

namespace ThisIsMyPC.App;

sealed class Program
{
    internal static IInstallationGuard? InstallGuard { get; private set; }

    [STAThread]
    public static void Main(string[] args)
    {
#if ACG_ENABLED
        // Guarded NativeAOT builds forbid new executable memory before UI startup.
        // Patched Avalonia and SkiaSharp use static unmanaged callbacks.
        if (!DynamicCodeHardening.Apply() || !DynamicCodeHardening.IsEnabled())
            Environment.FailFast("Arbitrary Code Guard could not be enabled.");
#endif

        // First: drop working directory and PATH from every DLL resolution in
        // the process (System32 + application dir only). Must precede any code
        // that could fault in a library.
        if (!DllSearchHardening.Apply())
            Environment.FailFast("Safe DLL search policy could not be enabled.");

        var installGuard = new InstallationGuard(AppContext.BaseDirectory);
#if !DEBUG
        if (!installGuard.IsProtectedLocation)
            Environment.FailFast(installGuard.WarningMessage ?? "The application directory is not protected.");
#endif

#if CIG_ENABLED
        // Optional Winsock providers can be non-Microsoft signed. CIG rejects
        // them, but Windows must return that failure instead of showing a
        // process-blocking Bad Image dialog.
        if (!CriticalErrorDialogHardening.Apply())
            Environment.FailFast("Critical error dialog suppression could not be enabled.");

        // CIG cannot admit our OV-signed Avalonia, Skia, HarfBuzz, and SQLite
        // images. Verify and map the fixed package set before closing image loads.
        var nativeDependencies = TrustedNativeDependencyLoader.VerifyAndLoad(AppContext.BaseDirectory);
        if (!nativeDependencies.IsSuccess)
            Environment.FailFast(nativeDependencies.ErrorMessage ?? "Native dependency verification failed.");
        if (!BinarySignatureHardening.Apply() || !BinarySignatureHardening.IsEnabled())
            Environment.FailFast("Code Integrity Guard could not be enabled.");
#endif

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

        var dataDir = AppConstants.UserDataDirectoryPath;
        Directory.CreateDirectory(dataDir);

#if DEBUG
        var log = LoggingSetup.Configure(dataDir, verbose: true, console: hasDebugConsole);
#else
        var log = LoggingSetup.Configure(dataDir, verbose: false, console: false);
#endif

#pragma warning disable CA1031 // Top-level crash handler must catch all exceptions
        // Faults on thread-pool threads and forgotten tasks never reach the
        // catch below; log them so the last line before a crash names it.
        AppDomain.CurrentDomain.UnhandledException += (_, e) =>
            log.Fatal(e.ExceptionObject as Exception, "Unhandled exception (terminating: {Terminating})", e.IsTerminating);
        TaskScheduler.UnobservedTaskException += (_, e) =>
            log.Error(e.Exception, "Unobserved task exception");

        try
        {
            log.Info("ThisIsMyPC starting");

            InstallGuard = installGuard;
            if (installGuard.IsProtectedLocation)
                log.Info("Installation path verified: {Path}", AppContext.BaseDirectory);
            else
                log.Warn("Unprotected install location: {Path}: {Warning}",
                    AppContext.BaseDirectory, installGuard.WarningMessage);

            // Older profile builds stored data under roaming AppData. Copy it once
            // before services open the local UI state.
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
    {
        var builder = AppBuilder.Configure<App>();

#if ACG_ENABLED
        // ANGLE creates a window under strict ACG but presents only black frames.
        return builder
            .UseWin32()
            .With(CreateAcgWin32Options())
            .UseSkia()
            .LogToTrace();
#else
        return builder
            .UsePlatformDetect()
            .LogToTrace();
#endif
    }

    internal static Win32PlatformOptions CreateAcgWin32Options() => new()
    {
        RenderingMode = [Win32RenderingMode.Software],
    };
}
