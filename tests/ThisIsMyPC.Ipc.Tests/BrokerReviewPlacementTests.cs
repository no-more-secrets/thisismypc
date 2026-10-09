using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using ThisIsMyPC.Broker;
using ThisIsMyPC.Ipc.Contracts;

namespace ThisIsMyPC.Ipc.Tests;

[Collection("NativeReviewWindow")]
public sealed partial class BrokerReviewPlacementTests
{
    [Fact]
    public void ReviewApplyButtonLinesUpWithAppApplyButton()
    {
        var position = BrokerReviewPlacement.Align(1200, 800, 1, 480, 540, 0, 0, 1920, 1080);

        Assert.Equal(1159, position.X + 480 - 16);
        Assert.Equal(726 - 8, position.Y + 540);

        var scaled = BrokerReviewPlacement.Align(1800, 1200, 1.5, 480, 540, 0, 0, 2400, 1600);
        Assert.Equal(1800 - (int)Math.Round(41 * 1.5), scaled.X + 480 - 16);
        Assert.Equal(1200 - (int)Math.Round(82 * 1.5), scaled.Y + 540);
    }

    [Fact]
    public void ReviewStaysInsideTheMonitorWorkArea()
    {
        var position = BrokerReviewPlacement.Align(950, 650, 1, 480, 540, 300, 200, 900, 700);

        Assert.Equal(420, position.X);
        Assert.Equal(200, position.Y);
    }

    [Fact]
    public void UserZoomIsBoundedBeforeItAffectsPlacement()
    {
        var directory = Path.Combine("artifacts", "diagnostics", "broker-placement");
        Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, Guid.NewGuid().ToString("N") + ".json");
        try
        {
            File.WriteAllText(path, """{"appSettings":{"uiZoom":"150"}}""");
            Assert.Equal(1.5, BrokerReviewZoom.FromSettingsFile(path));
            File.WriteAllText(path, """{"appSettings":{"uiZoom":"500"}}""");
            Assert.Equal(1.5, BrokerReviewZoom.FromSettingsFile(path));
            File.WriteAllText(path, "[]");
            Assert.Equal(1, BrokerReviewZoom.FromSettingsFile(path));
        }
        finally { File.Delete(path); }
    }

    [Fact]
    [Trait("Category", "Diagnostic")]
    public void BrokerFindsOnlyTheMatchingProcessWindow()
    {
        var window = CreateWindowExW(0, "STATIC", "ThisIsMyPC", 0x90000000,
            100, 100, 1000, 800, 0, 0, 0, 0);
        Assert.NotEqual(0, window);
        try
        {
            Assert.NotNull(BrokerReviewPlacement.ForUiProcess(Environment.ProcessId, 480, 540));
            Assert.Null(BrokerReviewPlacement.ForUiProcess(int.MaxValue, 480, 540));
        }
        finally { Assert.True(DestroyWindow(window)); }
    }

    [Fact]
    [Trait("Category", "Diagnostic")]
    public void NativeReviewOpensAboveTheAppApplyBar()
    {
        var appWindow = CreateWindowExW(0, "STATIC", "ThisIsMyPC", 0x90000000,
            100, 100, 1000, 800, 0, 0, 0, 0);
        Assert.NotEqual(0, appWindow);
        var policy = BrokerRequestPolicy.Create(new BrokerSessionRequest
        {
            ReviewOnly = [new() { DisplayName = "Local setting", Detail = "Off -> On" }],
        }).Value!;
        var review = Task.Run(() => NativeReviewWindow.Show(policy, Environment.ProcessId));
        nint reviewWindow = 0;
        try
        {
            for (var attempt = 0; attempt < 100 && reviewWindow == 0; attempt++)
            {
                reviewWindow = FindWindowW("ThisIsMyPCBrokerReview", null);
                Thread.Sleep(50);
            }
            Assert.NotEqual(0, reviewWindow);
            Assert.True(GetWindowRect(reviewWindow, out var bounds));
            var expected = BrokerReviewPlacement.ForUiProcess(Environment.ProcessId,
                bounds.Right - bounds.Left, bounds.Bottom - bounds.Top);
            Assert.NotNull(expected);
            Assert.Equal(expected.Value.X, bounds.Left);
            Assert.Equal(expected.Value.Y, bounds.Top);
            if (Environment.GetEnvironmentVariable("TIPC_PLACEMENT_SHOT") is { Length: > 0 } imagePath)
            {
                Thread.Sleep(250);
                Assert.True(GetWindowRect(appWindow, out var appBounds));
                using var bitmap = new Bitmap(appBounds.Right - appBounds.Left, appBounds.Bottom - appBounds.Top);
                using var reviewBitmap = new Bitmap(bounds.Right - bounds.Left, bounds.Bottom - bounds.Top);
                using (var graphics = Graphics.FromImage(bitmap))
                {
                    var device = graphics.GetHdc();
                    try { Assert.True(PrintWindow(appWindow, device, 2)); }
                    finally { graphics.ReleaseHdc(device); }
                    using var reviewGraphics = Graphics.FromImage(reviewBitmap);
                    device = reviewGraphics.GetHdc();
                    try { Assert.True(PrintWindow(reviewWindow, device, 2)); }
                    finally { reviewGraphics.ReleaseHdc(device); }
                    graphics.DrawImageUnscaled(reviewBitmap,
                        bounds.Left - appBounds.Left, bounds.Top - appBounds.Top);
                }
                Directory.CreateDirectory(Path.GetDirectoryName(imagePath)!);
                bitmap.Save(imagePath, ImageFormat.Png);
            }
        }
        finally
        {
            if (reviewWindow != 0)
                Assert.True(PostMessageW(reviewWindow, 0x0010, 0, 0));
            // The test window must be destroyed on the thread that created it.
#pragma warning disable xUnit1031
            Assert.Equal(NativeReviewWindow.Decision.Cancelled, review.GetAwaiter().GetResult());
#pragma warning restore xUnit1031
            Assert.True(DestroyWindow(appWindow));
        }
    }

    [LibraryImport("user32.dll", EntryPoint = "CreateWindowExW", StringMarshalling = StringMarshalling.Utf16)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    private static partial nint CreateWindowExW(uint exStyle, string className, string windowName,
        uint style, int x, int y, int width, int height, nint parent, nint menu, nint instance, nint parameter);
    [LibraryImport("user32.dll", EntryPoint = "DestroyWindow")]
    [return: MarshalAs(UnmanagedType.Bool)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    private static partial bool DestroyWindow(nint window);
    [LibraryImport("user32.dll", EntryPoint = "FindWindowW", StringMarshalling = StringMarshalling.Utf16)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    private static partial nint FindWindowW(string className, string? title);
    [LibraryImport("user32.dll", EntryPoint = "PostMessageW")]
    [return: MarshalAs(UnmanagedType.Bool)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    private static partial bool PostMessageW(nint window, uint message, nuint wParam, nint lParam);
    [StructLayout(LayoutKind.Sequential)]
    private struct Rect { internal int Left; internal int Top; internal int Right; internal int Bottom; }
    [LibraryImport("user32.dll", EntryPoint = "GetWindowRect")]
    [return: MarshalAs(UnmanagedType.Bool)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    private static partial bool GetWindowRect(nint window, out Rect bounds);
    [LibraryImport("user32.dll", EntryPoint = "PrintWindow")]
    [return: MarshalAs(UnmanagedType.Bool)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    private static partial bool PrintWindow(nint window, nint device, uint flags);
}
