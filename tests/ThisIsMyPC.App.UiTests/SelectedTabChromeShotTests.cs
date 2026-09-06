using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using SkiaSharp;
using ThisIsMyPC.App.Controls;

namespace ThisIsMyPC.App.UiTests;

public class SelectedTabChromeShotTests
{
    [AvaloniaFact]
    public void FilletStrokeHasNoSeamAtCommonDisplayScales()
    {
        var chrome = new SelectedTabChrome { Width = 160, Height = 35,
            Background = Brushes.White, BorderBrush = Brushes.Black };
        var root = new Canvas { Background = Brushes.White };
        Canvas.SetLeft(chrome, 40);
        Canvas.SetTop(chrome, 20);
        root.Children.Add(chrome);
        using var session = UiSession.ForView(root, new object(), "tab-continuous-outline", width: 240, height: 90);
        foreach (var scale in new[] { 1.0, 1.25, 1.5, 1.75, 2.0 })
        {
            using var frame = new RenderTargetBitmap(new PixelSize((int)(240 * scale), (int)(90 * scale)),
                new Vector(96 * scale, 96 * scale));
            frame.Render(session.Window);
            var path = Path.Combine(session.ShotDirectory, $"outline-{scale:0.00}.png");
            frame.Save(path);
            using var pixels = SKBitmap.Decode(path);
            // The floor fill must not erase either curve where it reaches the rim.
            foreach (var endpoint in new[] { 34, 206 })
            {
                var darkest = 255;
                for (var x = (int)((endpoint - 1) * scale); x <= (int)((endpoint + 1) * scale); x++)
                for (var y = (int)(54 * scale); y < (int)(55 * scale); y++)
                    darkest = Math.Min(darkest, pixels.GetPixel(x, y).Red);
                Assert.True(darkest < 200, $"Missing rim join at {endpoint}, scale {scale}.");
            }
            // Compare ink coverage through the straight-to-curve join with a one-pixel line.
            // Pixel coverage includes antialiasing, so fractional scaling remains measurable.
            foreach (var left in new[] { 34, 194 })
            for (var y = (int)(45 * scale); y < (int)(50 * scale); y++)
            {
                double ink = 0;
                for (var x = (int)(left * scale); x < (int)((left + 12) * scale); x++)
                    ink += (255 - pixels.GetPixel(x, y).Red) / 255.0;
                Assert.InRange(ink / scale, 0.8, 1.25);
            }
        }
    }
}
