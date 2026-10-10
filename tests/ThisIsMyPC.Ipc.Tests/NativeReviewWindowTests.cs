using System.Runtime.InteropServices;
using System.Drawing;
using System.Drawing.Imaging;
using ThisIsMyPC.Broker;
using ThisIsMyPC.Core.Changes;
using ThisIsMyPC.Core.Enforcement;
using ThisIsMyPC.Ipc.Contracts;

namespace ThisIsMyPC.Ipc.Tests;

[Collection("NativeReviewWindow")]
public sealed partial class NativeReviewWindowTests
{
    [Fact]
    public void ReviewFitsTheTargetMonitorAtTwoHundredPercent()
    {
        var size = NativeReviewWindow.FitToWorkArea(192, 1280, 720);

        Assert.NotNull(size);
        Assert.Equal(960, size.Value.Width);
        Assert.Equal(640, size.Value.Height);
    }

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
    public async Task CloseButtonKeepsTheReviewPending()
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
        Assert.Equal(0, GetWindowLongW(window, -16) & 0x00C00000);
        Assert.Equal(0, DwmGetWindowAttribute(window, 33, out var corners, sizeof(int)));
        Assert.Equal(2, corners);
        Assert.Equal(2, GetAwarenessFromDpiAwarenessContext(GetWindowDpiAwarenessContext(window)));
        Assert.True(GetWindowRect(window, out var bounds));
        var headerPoint = (nint)((((bounds.Top + 20) & 0xffff) << 16) | ((bounds.Left + 40) & 0xffff));
        Assert.Equal((nint)2, SendMessageW(window, 0x0084, 0, headerPoint));
        var button = GetDlgItem(window, 1003);
        Assert.NotEqual(0, button);
        Assert.True(PostMessageW(button, 0x0201, 1, (nint)((14 << 16) | 14)));
        Assert.True(PostMessageW(button, 0x0202, 0, (nint)((14 << 16) | 14)));

        Assert.Equal(NativeReviewWindow.Decision.Cancelled, await review);
    }

    [Fact]
    [Trait("Category", "Diagnostic")]
    public async Task EscapeKeepsTheReviewPending()
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
        Assert.True(PostMessageW(button, 0x0100, 0x1B, 0));

        Assert.Equal(NativeReviewWindow.Decision.Cancelled, await review);
    }

    [Fact]
    [Trait("Category", "Diagnostic")]
    public async Task ArrowKeysDoNotSelectOrTriggerReviewButtons()
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
        try
        {
            foreach (var id in new[] { 1001, 1002, 1003 })
            {
                var button = GetDlgItem(window, id);
                Assert.NotEqual(0, button);
                Assert.Equal(0, GetWindowLongW(button, -16) & 0x00010000);
            }
            Assert.True(PostMessageW(window, 0x0100, 0x27, 0));
            Assert.True(PostMessageW(window, 0x0100, 0x0D, 0));
            Assert.True(PostMessageW(window, 0x0100, 0x20, 0));
            await Task.Delay(150);
            Assert.False(review.IsCompleted);
        }
        finally
        {
            Assert.True(PostMessageW(window, 0x0010, 0, 0));
        }
        Assert.Equal(NativeReviewWindow.Decision.Cancelled, await review);
    }

    [Fact]
    [Trait("Category", "Diagnostic")]
    public async Task DpiChangeResizesTheReviewButtons()
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
        var suggested = Marshal.AllocHGlobal(Marshal.SizeOf<Rect>());
        var previousDpiContext = SetThreadDpiAwarenessContext((nint)(-4));
        try
        {
            Assert.True(GetWindowRect(window, out var bounds));
            Marshal.StructureToPtr(new Rect
            {
                Left = bounds.Left,
                Top = bounds.Top,
                Right = bounds.Left + 960,
                Bottom = bounds.Top + 1080,
            }, suggested, false);
            _ = SendMessageW(window, 0x02E0, (nuint)(192 | (192 << 16)), suggested);
            var apply = GetDlgItem(window, 1001);
            Assert.NotEqual(0, apply);
            Assert.True(GetClientRect(apply, out var button));
            Assert.Equal(224, button.Right);
            Assert.Equal(72, button.Bottom);
            Assert.True(GetWindowRect(window, out var scaledBounds));
            var work = BrokerReviewPlacement.WorkAreaForRect(scaledBounds.Left, scaledBounds.Top,
                scaledBounds.Right, scaledBounds.Bottom);
            Assert.NotNull(work);
            Assert.InRange(scaledBounds.Left, work.Value.Left, work.Value.Right - 1);
            Assert.InRange(scaledBounds.Top, work.Value.Top, work.Value.Bottom - 1);
            Assert.True(scaledBounds.Right <= work.Value.Right);
            Assert.True(scaledBounds.Bottom <= work.Value.Bottom);
            if (Environment.GetEnvironmentVariable("TIPC_REVIEW_DPI_SHOT") is { Length: > 0 } imagePath)
            {
                using var bitmap = new Bitmap(scaledBounds.Right - scaledBounds.Left,
                    scaledBounds.Bottom - scaledBounds.Top);
                using var graphics = Graphics.FromImage(bitmap);
                var device = graphics.GetHdc();
                try { Assert.True(PrintWindow(window, device, 2)); }
                finally { graphics.ReleaseHdc(device); }
                Directory.CreateDirectory(Path.GetDirectoryName(imagePath)!);
                bitmap.Save(imagePath, ImageFormat.Png);
            }
        }
        finally
        {
            if (previousDpiContext != 0)
                _ = SetThreadDpiAwarenessContext(previousDpiContext);
            Marshal.FreeHGlobal(suggested);
            Assert.True(PostMessageW(window, 0x0010, 0, 0));
        }
        Assert.Equal(NativeReviewWindow.Decision.Cancelled, await review);
    }

    [Fact]
    [Trait("Category", "Diagnostic")]
    public async Task CloseCancelsTheNativeReview()
    {
        var policy = BrokerRequestPolicy.Create(new BrokerSessionRequest
        {
            Changes = [new ChangeDescriptor
            {
                ModuleId = "Windows Update",
                SettingId = "no-auto-reboot",
                DisplayName = "Do not restart for updates while signed in",
                SystemLocation = @"HKLM\SOFTWARE\Policies\Microsoft\Windows\WindowsUpdate\AU\NoAutoRebootWithLoggedOnUsers",
                BeforeValue = "0",
                AfterValue = "1",
                BeforeDisplay = "Off",
                AfterDisplay = "On",
                ValueType = ChangeValueType.Registry_DWord,
                Enforcement = new SettingEnforcement
                {
                    GPCacheEntries = [@"HKLM\SOFTWARE\Microsoft\WindowsUpdate\UpdatePolicy\GPCache"],
                    ReversionVectors = ["Group Policy refresh"],
                    SkuRestriction = ThisIsMyPC.Core.Modules.WindowsSku.Pro,
                },
            }],
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
                var previousDpiContext = SetThreadDpiAwarenessContext((nint)(-4));
                try
                {
                    Assert.True(GetWindowRect(window, out var bounds));
                    using var bitmap = new Bitmap(bounds.Right - bounds.Left, bounds.Bottom - bounds.Top);
                    using var graphics = Graphics.FromImage(bitmap);
                    var device = graphics.GetHdc();
                    try { Assert.True(PrintWindow(window, device, 2)); }
                    finally { graphics.ReleaseHdc(device); }
                    Directory.CreateDirectory(Path.GetDirectoryName(imagePath)!);
                    bitmap.Save(imagePath, ImageFormat.Png);
                }
                finally
                {
                    if (previousDpiContext != 0)
                        _ = SetThreadDpiAwarenessContext(previousDpiContext);
                }
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

    [Fact]
    [Trait("Category", "Diagnostic")]
    public async Task LongReviewCanScrollToItsLastCard()
    {
        var policy = BrokerRequestPolicy.Create(new BrokerSessionRequest
        {
            ReviewOnly = Enumerable.Range(1, 30).Select(index => new BrokerReviewItem
            {
                DisplayName = $"Desktop change {index}",
                Detail = $"Review operation {index}",
            }).ToList(),
        }).Value!;
        var review = Task.Run(() => NativeReviewWindow.Show(policy));
        nint window = 0;
        for (var attempt = 0; attempt < 100 && window == 0; attempt++)
        {
            window = FindWindowW("ThisIsMyPCBrokerReview", null);
            await Task.Delay(50);
        }
        Assert.NotEqual(0, window);
        var previousDpiContext = SetThreadDpiAwarenessContext((nint)(-4));
        try
        {
            Assert.True(GetWindowRect(window, out var bounds));
            var scrollbarIsHit = Enumerable.Range(1, 40).Any(inset =>
            {
                var point = (nint)((((bounds.Top + (bounds.Bottom - bounds.Top) / 2) & 0xffff) << 16)
                    | ((bounds.Right - inset) & 0xffff));
                return SendMessageW(window, 0x0084, 0, point) == (nint)7;
            });
            Assert.True(scrollbarIsHit);
        }
        finally
        {
            if (previousDpiContext != 0)
                _ = SetThreadDpiAwarenessContext(previousDpiContext);
        }
        _ = SendMessageW(window, 0x0115, 7, 0);
        var scroll = new ScrollInfo { Size = (uint)Marshal.SizeOf<ScrollInfo>(), Mask = 4 };
        Assert.True(GetScrollInfo(window, 1, ref scroll));
        Assert.True(scroll.Position > 0);
        Assert.True(PostMessageW(window, 0x0010, 0, 0));
        Assert.Equal(NativeReviewWindow.Decision.Cancelled, await review);
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

    [StructLayout(LayoutKind.Sequential)]
    private struct ScrollInfo
    {
        internal uint Size;
        internal uint Mask;
        internal int Minimum;
        internal int Maximum;
        internal uint Page;
        internal int Position;
        internal int TrackPosition;
    }

    [LibraryImport("user32.dll", EntryPoint = "GetScrollInfo")]
    [return: MarshalAs(UnmanagedType.Bool)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    private static partial bool GetScrollInfo(nint window, int bar, ref ScrollInfo info);

    [LibraryImport("user32.dll", EntryPoint = "GetWindowRect")]
    [return: MarshalAs(UnmanagedType.Bool)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    private static partial bool GetWindowRect(nint window, out Rect rectangle);
    [LibraryImport("user32.dll", EntryPoint = "GetClientRect")]
    [return: MarshalAs(UnmanagedType.Bool)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    private static partial bool GetClientRect(nint window, out Rect rectangle);

    [LibraryImport("user32.dll", EntryPoint = "GetWindowLongW")]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    private static partial int GetWindowLongW(nint window, int index);

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
    [LibraryImport("dwmapi.dll", EntryPoint = "DwmGetWindowAttribute")]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    private static partial int DwmGetWindowAttribute(nint window, uint attribute, out int value, uint size);
    [LibraryImport("user32.dll", EntryPoint = "GetWindowDpiAwarenessContext")]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    private static partial nint GetWindowDpiAwarenessContext(nint window);
    [LibraryImport("user32.dll", EntryPoint = "GetAwarenessFromDpiAwarenessContext")]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    private static partial int GetAwarenessFromDpiAwarenessContext(nint context);
    [LibraryImport("user32.dll", EntryPoint = "SetThreadDpiAwarenessContext")]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    private static partial nint SetThreadDpiAwarenessContext(nint context);
}
