using SkiaSharp;

namespace ThisIsMyPC.App.UiTests;

/// <summary>
/// The edge-geometry contract measured in pixels: a straight port of
/// tools/measure-edge-geometry.ps1 for suites that cannot shell out. Dark
/// theme only (Base #1a1a2e window, Raised #242438 card, Outline #3f3f5a).
/// Expected on every page: ContentL 25, ContentR 23, LaneFrom 10, ContentT 17;
/// with an edge tab strip pass its height as skipHeaderPixels and ContentT reads 59.
/// </summary>
public sealed record EdgeGeometry(
    string Name, int CardL, int CardR, int CardT, int CardB,
    int ContentL, int ContentR, int ContentT, int ContentB, int LaneFrom, int LaneTo)
{
    private static bool IsBase(SKColor c) => c.Red == 26 && c.Green == 26 && c.Blue == 46;

    private static bool IsBackground(SKColor c)
    {
        foreach (var (r, g, b) in new[] { (26, 26, 46), (36, 36, 56), (63, 63, 90) })
        {
            if (Math.Abs(c.Red - r) <= 3 && Math.Abs(c.Green - g) <= 3 && Math.Abs(c.Blue - b) <= 3)
                return true;
        }
        return false;
    }

    public static EdgeGeometry Measure(string path, int skipHeaderPixels = 0)
    {
        using var bmp = SKBitmap.Decode(path) ?? throw new InvalidOperationException($"Cannot decode {path}");
        var w = bmp.Width;
        var h = bmp.Height;

        var cardTop = -1;
        for (var y = 60; y < 300; y++)
        {
            if (IsBase(bmp.GetPixel(700, y)))
                continue;
            var run = 0;
            for (var k = y; k < Math.Min(y + 60, h); k++)
            {
                if (!IsBase(bmp.GetPixel(700, k))) run++;
                else break;
            }
            if (run >= 60) { cardTop = y; break; }
        }
        var rowY = cardTop + 300;
        var cardRight = -1;
        for (var x = w - 4; x > 400; x--)
        {
            if (!IsBase(bmp.GetPixel(x, rowY))) { cardRight = x; break; }
        }
        var cardLeft = -1;
        for (var x = 205; x < 700; x++)
        {
            if (!IsBase(bmp.GetPixel(x, rowY))) { cardLeft = x; break; }
        }
        var cardBottom = -1;
        for (var y = rowY; y < h; y++)
        {
            if (IsBase(bmp.GetPixel(700, y))) { cardBottom = y - 1; break; }
        }

        int minX = -1, maxX = -1, laneMin = -1, laneMax = -1;
        for (var y = cardTop + Math.Max(14, skipHeaderPixels); y < cardBottom - 14; y += 2)
        {
            for (var x = cardLeft + 3; x < cardRight - 2; x++)
            {
                if (IsBackground(bmp.GetPixel(x, y))) continue;
                if (x >= cardRight - 18)
                {
                    if (laneMin < 0 || x < laneMin) laneMin = x;
                    if (x > laneMax) laneMax = x;
                    continue;
                }
                if (minX < 0 || x < minX) minX = x;
                if (x > maxX) maxX = x;
            }
        }

        int minY = -1, maxY = -1;
        for (var y = cardTop + Math.Max(3, skipHeaderPixels); y < cardBottom - 2; y++)
        {
            for (var x = cardLeft + 14; x < cardRight - 14; x += 2)
            {
                if (IsBackground(bmp.GetPixel(x, y))) continue;
                if (minY < 0) minY = y;
                maxY = y;
                break;
            }
        }

        return new EdgeGeometry(
            Path.GetFileNameWithoutExtension(path),
            cardLeft, cardRight, cardTop, cardBottom,
            ContentL: minX - cardLeft,
            ContentR: cardRight - maxX,
            ContentT: minY - cardTop,
            ContentB: cardBottom - maxY,
            LaneFrom: laneMin >= 0 ? cardRight - laneMax : -1,
            LaneTo: laneMin >= 0 ? cardRight - laneMin : -1);
    }
}
