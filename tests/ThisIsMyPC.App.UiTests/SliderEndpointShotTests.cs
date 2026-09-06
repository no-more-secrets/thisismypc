using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Headless.XUnit;
using Avalonia.Styling;
using Avalonia.VisualTree;

namespace ThisIsMyPC.App.UiTests;

public class SliderEndpointShotTests
{
    [AvaloniaFact]
    public void HoveredThumbRemainsVisibleAtBothEndpoints()
    {
        var slider = new Slider { Minimum = 0, Maximum = 100, Width = 300 };
        using var session = UiSession.ForView(new Border { Padding = new Thickness(30), Child = slider },
            new object(), "slider-endpoints", width: 400, height: 120);
        foreach (var theme in new[] { ThemeVariant.Dark, ThemeVariant.Light })
        foreach (var value in new[] { 0, 100 })
        {
            session.SetTheme(theme);
            slider.Value = value;
            session.Pump();

            var thumb = slider.GetVisualDescendants().OfType<Thumb>().Single();
            // Disable animation so the screenshot and bounds use the final hover size.
            thumb.Transitions = null;
            session.Hover(slider);
            session.Pump();
            Assert.True(slider.IsPointerOver);
            Assert.Equal(1.3, thumb.RenderTransform!.Value.M11, 0.01);
            foreach (var ancestor in thumb.GetVisualAncestors().TakeWhile(v => v != session.Window))
            {
                if (!ancestor.ClipToBounds) continue;
                var topLeft = thumb.TranslatePoint(default, ancestor)!.Value;
                var bottomRight = thumb.TranslatePoint(new Point(thumb.Bounds.Width, thumb.Bounds.Height), ancestor)!.Value;
                Assert.True(topLeft.X >= 0 && topLeft.Y >= 0 && bottomRight.X <= ancestor.Bounds.Width && bottomRight.Y <= ancestor.Bounds.Height,
                    $"{ancestor.GetType().Name} clips the hovered thumb: {topLeft} to {bottomRight}, bounds {ancestor.Bounds}");
            }
            session.Screenshot($"{theme.Key}-{value}");
        }
    }
}
