using Avalonia;
using ThisIsMyPC.Interop.Win32.Security;

namespace ThisIsMyPC.Installer;

sealed class Program
{
    [STAThread]
    public static int Main(string[] args)
    {
#if ACG_ENABLED
        if (!DynamicCodeHardening.Apply() || !DynamicCodeHardening.IsEnabled())
            Environment.FailFast("Arbitrary Code Guard could not be enabled.");
#endif

        // Same first step as the app: no working-directory or PATH DLL
        // resolution in an elevated process.
#if ACG_ENABLED
        if (!DllSearchHardening.Apply(includeApplicationDirectory: false))
#else
        if (!DllSearchHardening.Apply())
#endif
            Environment.FailFast("Safe DLL search policy could not be enabled.");

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
    {
        var builder = AppBuilder.Configure<App>();

#if ACG_ENABLED
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
