using Avalonia.Headless.XUnit;
using Avalonia.Styling;
using ThisIsMyPC.App.ViewModels;
using ThisIsMyPC.App.Views;

namespace ThisIsMyPC.App.UiTests;

/// <summary>
/// CI-safe: renders the UI Gallery (the one-page style reference) in both
/// themes. If a standardized class or token breaks, this page shows it first.
/// </summary>
public class GalleryViewShotTests
{
    [AvaloniaFact]
    public void Gallery_RendersInBothThemes()
    {
        var viewModel = new GalleryViewModel();
        using var session = UiSession.ForView(new GalleryView(), viewModel, "gallery");

        try
        {
            session.Screenshot("gallery-dark");
            Assert.True(session.IsTextVisible("Type scale"));
            Assert.True(session.IsTextVisible("Scope badges"));

            session.SetTheme(ThemeVariant.Light);
            session.Screenshot("gallery-light");
            Assert.True(session.IsTextVisible("Color tokens"));

            var scroller = session.Find<Avalonia.Controls.ScrollViewer>(_ => true);
            scroller.Offset = new Avalonia.Vector(0, 300);
            session.Screenshot("gallery-light-mid");
            scroller.ScrollToEnd();
            session.Screenshot("gallery-light-bottom");
            session.SetTheme(ThemeVariant.Dark);
            session.Screenshot("gallery-dark-bottom");
        }
        finally
        {
            session.SetTheme(ThemeVariant.Dark);
        }
    }
}
