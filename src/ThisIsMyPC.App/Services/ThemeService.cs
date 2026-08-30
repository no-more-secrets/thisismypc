using Avalonia;
using Avalonia.Styling;

namespace ThisIsMyPC.App.Services;

/// <summary>
/// Maps the persisted theme setting to Application.RequestedThemeVariant.
/// The palette lives in Styles/Theme.axaml as theme dictionaries; consumers use
/// DynamicResource, so applying a variant restyles the running app.
/// </summary>
public static class ThemeService
{
    public const string Dark = "dark";
    public const string Light = "light";
    public const string System = "system";

    public static void Apply(string theme)
    {
        if (Application.Current is not { } app)
            return;

        app.RequestedThemeVariant = theme switch
        {
            Light => ThemeVariant.Light,
            System => ThemeVariant.Default,
            _ => ThemeVariant.Dark,
        };
    }
}
