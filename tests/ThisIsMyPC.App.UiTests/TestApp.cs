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
/// Headless twin of the production App: identical styles and resources loaded
/// from the App assembly, none of the startup side effects (tray, monitoring,
/// data-directory hardening, real service graph). UseHeadlessDrawing=false
/// turns on real Skia rendering so screenshots are true pixels.
/// </summary>
public static class TestAppBuilder
{
    public static AppBuilder BuildAvaloniaApp() => AppBuilder
        .Configure<TestApp>()
        .UseSkia()
        .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false });
}

public sealed class TestApp : Application
{
    public override void Initialize()
    {
        RequestedThemeVariant = ThemeVariant.Dark;

        Styles.Add(new FluentTheme());
        Styles.Add(new StyleInclude(new Uri("avares://ThisIsMyPC.App/"))
        {
            Source = new Uri("avares://ThisIsMyPC.App/Styles/Typography.axaml"),
        });
        Styles.Add(new StyleInclude(new Uri("avares://ThisIsMyPC.App/"))
        {
            Source = new Uri("avares://ThisIsMyPC.App/Styles/Controls.axaml"),
        });

        var resources = new ResourceDictionary();
        resources.MergedDictionaries.Add(new ResourceInclude(new Uri("avares://ThisIsMyPC.App/"))
        {
            Source = new Uri("avares://ThisIsMyPC.App/Styles/Theme.axaml"),
        });
        resources.MergedDictionaries.Add(new ResourceInclude(new Uri("avares://ThisIsMyPC.App/"))
        {
            Source = new Uri("avares://ThisIsMyPC.App/Templates/ToggleSettingRowTemplate.axaml"),
        });
        resources.MergedDictionaries.Add(new ResourceInclude(new Uri("avares://ThisIsMyPC.App/"))
        {
            Source = new Uri("avares://ThisIsMyPC.App/Templates/MultiScopeRowTemplate.axaml"),
        });
        resources.MergedDictionaries.Add(new ResourceInclude(new Uri("avares://ThisIsMyPC.App/"))
        {
            Source = new Uri("avares://ThisIsMyPC.App/Templates/ChoiceSettingRowTemplate.axaml"),
        });
        Resources = resources;
    }
}
