using System.Runtime.InteropServices;
using System.Drawing;
using System.Drawing.Imaging;
using ThisIsMyPC.Broker;
using ThisIsMyPC.Ipc.Contracts;

namespace ThisIsMyPC.Ipc.Tests;

public sealed partial class NativeReviewWindowTests
{
    [Fact]
    [Trait("Category", "Diagnostic")]
    public async Task ApplyButtonApprovesTheNativeReview()
    {
        var policy = BrokerRequestPolicy.Create(new BrokerSessionRequest
        {
            ReviewOnly = [new() { DisplayName = "Local setting", Detail = "Off -> On" }],
        }).Value!;
        var review = Task.Run(() => NativeReviewWindow.Show(policy));
        nint window = 0;
        for (var attempt = 0; attempt < 100 && window == 0; attempt++)
        {
            window = FindWindowW("ThisIsMyPCBrokerReview", null);
            await Task.Delay(50);
        }
        Assert.NotEqual(0, window);
        var button = GetDlgItem(window, 1001);
        Assert.NotEqual(0, button);
        _ = SendMessageW(button, 0x00F5, 0, 0);

        Assert.Equal(NativeReviewWindow.Decision.Apply, await review);
    }

    [Fact]
    [Trait("Category", "Diagnostic")]
    public async Task DiscardButtonDiscardsTheNativeReview()
    {
        var policy = BrokerRequestPolicy.Create(new BrokerSessionRequest
        {
            ReviewOnly = [new() { DisplayName = "Local setting", Detail = "Off -> On" }],
        }).Value!;
        var review = Task.Run(() => NativeReviewWindow.Show(policy));
        nint window = 0;
        for (var attempt = 0; attempt < 100 && window == 0; attempt++)
        {
            window = FindWindowW("ThisIsMyPCBrokerReview", null);
            await Task.Delay(50);
        }
        Assert.NotEqual(0, window);
        var button = GetDlgItem(window, 1002);
        Assert.NotEqual(0, button);
        _ = SendMessageW(button, 0x00F5, 0, 0);

        Assert.Equal(NativeReviewWindow.Decision.Discarded, await review);
    }

    [Fact]
    [Trait("Category", "Diagnostic")]
    public async Task CloseCancelsTheNativeReview()
    {
        var policy = BrokerRequestPolicy.Create(new BrokerSessionRequest
        {
            ReviewOnly =
            [
                new() { DisplayName = "Protect sign-in passwords", Detail = "Off -> On" },
                new() { DisplayName = "Remove a desktop app", Detail = "Example application" },
            ],
        }).Value!;

        var close = Task.Run(async () =>
        {
            nint window = 0;
            for (var attempt = 0; attempt < 100 && window == 0; attempt++)
            {
                window = FindWindowW("ThisIsMyPCBrokerReview", null);
                await Task.Delay(50);
            }
            Assert.NotEqual(0, window);
            if (Environment.GetEnvironmentVariable("TIPC_REVIEW_SHOT") is { Length: > 0 } imagePath)
            {
                await Task.Delay(250);
                Assert.True(GetWindowRect(window, out var bounds));
                using var bitmap = new Bitmap(bounds.Right - bounds.Left, bounds.Bottom - bounds.Top);
                using var graphics = Graphics.FromImage(bitmap);
                var device = graphics.GetHdc();
                try { Assert.True(PrintWindow(window, device, 2)); }
                finally { graphics.ReleaseHdc(device); }
                Directory.CreateDirectory(Path.GetDirectoryName(imagePath)!);
                bitmap.Save(imagePath, ImageFormat.Png);
            }
            var previewDelay = int.TryParse(Environment.GetEnvironmentVariable("TIPC_REVIEW_PREVIEW_MS"), out var milliseconds)
                ? Math.Clamp(milliseconds, 0, 30_000)
                : 500;
            await Task.Delay(previewDelay);
            Assert.True(PostMessageW(window, 0x0010, 0, 0));
        });

        var review = Task.Run(() => NativeReviewWindow.Show(policy));
        await close;
        var decision = await review;
        Assert.Equal(NativeReviewWindow.Decision.Cancelled, decision);
    }

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
    private static partial bool GetWindowRect(nint window, out Rect rectangle);

    [LibraryImport("user32.dll", EntryPoint = "PrintWindow")]
    [return: MarshalAs(UnmanagedType.Bool)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    private static partial bool PrintWindow(nint window, nint device, uint flags);

    [LibraryImport("user32.dll", EntryPoint = "GetDlgItem")]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    private static partial nint GetDlgItem(nint window, int id);

    [LibraryImport("user32.dll", EntryPoint = "SendMessageW")]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    private static partial nint SendMessageW(nint window, uint message, nuint wParam, nint lParam);
}
