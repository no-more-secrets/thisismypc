using System.Text.RegularExpressions;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Markup.Xaml.Styling;
using Avalonia.Styling;
using Avalonia.Themes.Fluent;
using ThisIsMyPC.App.UiTests;

[assembly: AvaloniaTestApplication(typeof(TestAppBuilder))]

namespace ThisIsMyPC.App.UiTests;

/// <summary>
/// Headless twin of the production App: the styles and resources App.axaml
/// lists, read from that file so the twin cannot drift (a missing include
/// once left every ToggleCard without a template in the walkthrough while a
/// single-view test rendered it), none of the startup side effects (tray,
/// monitoring, data-directory hardening, real service graph).
/// UseHeadlessDrawing=false turns on real Skia rendering so screenshots are
/// true pixels.
/// </summary>
public static class TestAppBuilder
{
    public static AppBuilder BuildAvaloniaApp() => AppBuilder
        .Configure<TestApp>()
        .UseSkia()
        .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false });
}

public sealed partial class TestApp : Application
{
    private static readonly Uri AppBase = new("avares://ThisIsMyPC.App/");

    public override void Initialize()
    {
        RequestedThemeVariant = ThemeVariant.Dark;

        var appXaml = File.ReadAllText(Path.Combine(UiSession.FindRepoRoot(), "src", "ThisIsMyPC.App", "App.axaml"));

        Styles.Add(new FluentTheme());
        foreach (var source in Includes("StyleInclude", appXaml))
            Styles.Add(new StyleInclude(AppBase) { Source = new Uri(source) });

        var resources = new ResourceDictionary();
        foreach (var source in Includes("ResourceInclude", appXaml))
            resources.MergedDictionaries.Add(new ResourceInclude(AppBase) { Source = new Uri(source) });
        Resources = resources;
    }

    /// <summary>Every avares Source of the given include element, in file order.</summary>
    public static IReadOnlyList<string> Includes(string element, string appXaml) =>
        Regex.Matches(appXaml, $@"<{element}\s+Source=""(avares://[^""]+)""")
            .Select(m => m.Groups[1].Value)
            .ToList();
}
