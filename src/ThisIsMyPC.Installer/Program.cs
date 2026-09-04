using Avalonia;
using ThisIsMyPC.Interop.Win32.Security;

namespace ThisIsMyPC.Installer;

sealed class Program
{
    [STAThread]
    public static int Main(string[] args)
    {
        // Same first step as the app: no working-directory or PATH DLL
        // resolution in an elevated process.
        DllSearchHardening.Apply();

#pragma warning disable CA1031 // Last resort: a crash must show words, not vanish (NativeAOT fail-fasts silently).
        try
        {
#if !DEBUG
            var executablePath = Environment.ProcessPath
                ?? throw new InvalidOperationException("The installer executable path is unavailable.");
            var trust = AuthenticodeVerifier.VerifyTrusted(
                executablePath,
                "No More Secrets, LLC",
                exactSignerName: true);
            if (!trust.IsSuccess)
                throw new InvalidOperationException(
                    "The installer signature is invalid. This file may be incomplete or modified.\n\n" +
                    trust.ErrorMessage);
#endif
            NativeBootstrap.Prepare();
            BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
            return 0;
        }
        catch (Exception ex)
        {
            NativeBootstrap.ReportFatal("The installer could not start.\n\n" + ex.Message);
            return 1;
        }
#pragma warning restore CA1031
    }

    public static AppBuilder BuildAvaloniaApp()
        => AppBuilder.Configure<App>()
            .UsePlatformDetect()
            .LogToTrace();
}
