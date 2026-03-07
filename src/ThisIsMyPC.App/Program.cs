using Avalonia;
using Serilog;
using Serilog.Events;
using System;
using System.Globalization;
using System.IO;

namespace ThisIsMyPC.App;

sealed class Program
{
    [STAThread]
    public static void Main(string[] args)
    {
        var logPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "ThisIsMyPC", "logs", "thisismypc-.log");

        Log.Logger = new LoggerConfiguration()
#if DEBUG
            .MinimumLevel.Verbose()
#else
            .MinimumLevel.Information()
            .MinimumLevel.Override("ThisIsMyPC.Interop", LogEventLevel.Warning)
#endif
            .WriteTo.File(
                logPath,
                rollingInterval: RollingInterval.Day,
                retainedFileCountLimit: 7,
                fileSizeLimitBytes: 10 * 1024 * 1024,
                formatProvider: CultureInfo.InvariantCulture)
            .CreateLogger();

#pragma warning disable CA1031 // Top-level crash handler must catch all exceptions
        try
        {
            Log.Information("ThisIsMyPC starting");
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
