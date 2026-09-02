using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.VisualTree;
using Avalonia.Styling;
using ThisIsMyPC.App.UiTests.Fakes;
using ThisIsMyPC.App.ViewModels;
using ThisIsMyPC.App.Views;
using ThisIsMyPC.Core.Display;
using ThisIsMyPC.Core.Results;
using ThisIsMyPC.Core.Services;
using ThisIsMyPC.Modules.Display.Models;

namespace ThisIsMyPC.App.UiTests;

/// <summary>CI-safe: renders the Display module with fake monitors in both themes.</summary>
public class DisplayViewShotTests
{
    private sealed class StubMonitorService : IMonitorService
    {
        public List<string> Writes { get; } = [];

        public OperationResult<IReadOnlyList<MonitorDevice>> EnumerateMonitors() =>
            OperationResult<IReadOnlyList<MonitorDevice>>.Success([]);

        public OperationResult<bool> SetBrightness(string monitorId, int value)
        {
            Writes.Add($"brightness:{monitorId}={value}");
            return OperationResult<bool>.Success(true);
        }

        public OperationResult<bool> SetContrast(string monitorId, int value)
        {
            Writes.Add($"contrast:{monitorId}={value}");
            return OperationResult<bool>.Success(true);
        }

        public OperationResult<bool> SetInputSource(string monitorId, int value)
        {
            Writes.Add($"input:{monitorId}={value}");
            return OperationResult<bool>.Success(true);
        }

        public OperationResult<bool> SetVcpValue(string monitorId, int vcpCode, int value)
        {
            Writes.Add($"vcp:{monitorId}:0x{vcpCode:X2}={value}");
            return OperationResult<bool>.Success(true);
        }

        public OperationResult<bool> ReapplyLastWrites() => OperationResult<bool>.Success(true);

        public bool HasSystemBattery() => false;
    }


    private static DisplayScanData SampleData() => new(
        [
            new MonitorDevice
            {
                Id = "internal-panel",
                Name = "Built-in display",
                IsInternalPanel = true,
                SupportsDdc = true,
                Brightness = 70,
            },
            new MonitorDevice
            {
                Id = @"\\.\DISPLAY2|0",
                Name = "ASUS VG27AQ",
                SupportsDdc = true,
                Brightness = 55,
                Contrast = 80,
                CurrentInput = 0x11,
                PowerOffValue = 0x04,
                InputSources =
                [
                    new MonitorInputSource(0x0F, "DisplayPort 1"),
                    new MonitorInputSource(0x11, "HDMI 1"),
                    new MonitorInputSource(0x12, "HDMI 2"),
                ],
                VendorFeatures =
                [
                    new VendorVcpFeature(0xE6, "Blue light filter", [0, 1, 2, 3, 4], Current: 1, IsNamed: true),
                    new VendorVcpFeature(0xE0, "Feature 0xE0", [0, 2, 5], Current: 2),
                ],
            },
            new MonitorDevice
            {
                Id = @"\\.\DISPLAY3|0",
                Name = "Older monitor",
                SupportsDdc = false,
                DdcError = "This monitor did not answer DDC/CI. Some monitors need it enabled in their on-screen menu.",
            },
        ],
        ScanError: null);

    [AvaloniaFact]
    public void DisplayView_RendersMonitorsInBothThemes()
    {
        var monitors = new StubMonitorService();
        var viewModel = new DisplayViewModel(SampleData(), monitors, new UiFakePowerService());
        using var session = UiSession.ForView(new DisplayView(), viewModel, "display-view");

        try
        {
            session.Screenshot("display-dark");
            Assert.True(session.IsTextVisible("Built-in display"));
            Assert.True(session.IsTextVisible("ASUS VG27AQ"));

            session.SetTheme(ThemeVariant.Light);
            session.Screenshot("display-light");
            Assert.True(session.IsTextVisible("Older monitor"));
        }
        finally
        {
            session.SetTheme(ThemeVariant.Dark);
        }
    }

    [AvaloniaFact]
    public void MovingTheBrightnessSlider_WritesThroughTheService()
    {
        var monitors = new StubMonitorService();
        var viewModel = new DisplayViewModel(SampleData(), monitors, new UiFakePowerService());
        using var session = UiSession.ForView(new DisplayView(), viewModel, "display-view");

        var external = viewModel.Monitors[1];
        external.Brightness = 30;

        // The write coalescer runs on the thread pool; give it a moment.
        for (var i = 0; i < 200 && monitors.Writes.Count == 0; i++)
        {
            session.Pump();
            Thread.Sleep(10);
        }

        Assert.Contains(monitors.Writes, w => w == @"brightness:\\.\DISPLAY2|0=30");
    }

    [AvaloniaFact]
    public void MovingAVendorFeatureSlider_WritesItsVcpCode()
    {
        var monitors = new StubMonitorService();
        var viewModel = new DisplayViewModel(SampleData(), monitors, new UiFakePowerService());
        using var session = UiSession.ForView(new DisplayView(), viewModel, "display-view");

        var blueLight = Assert.Single(viewModel.Monitors[1].VendorFeatures);
        var gappyFeature = Assert.Single(viewModel.Monitors[1].AdvancedVendorFeatures);
        Assert.Equal("Blue light filter", blueLight.Name);
        Assert.True(blueLight.IsSlider); // 0-4 contiguous
        blueLight.Value = 4;

        for (var i = 0; i < 200 && monitors.Writes.Count == 0; i++)
        {
            session.Pump();
            Thread.Sleep(10);
        }

        Assert.Contains(monitors.Writes, w => w == @"vcp:\\.\DISPLAY2|0:0xE6=4");
        Assert.True(gappyFeature.IsCombo); // 0, 2, 5 is gappy: combo, not slider
    }

    [AvaloniaFact]
    public void DraggingTheBrightnessSlider_WithRealMouse_WritesThroughTheService()
    {
        var monitors = new StubMonitorService();
        var viewModel = new DisplayViewModel(SampleData(), monitors, new UiFakePowerService());
        using var session = UiSession.ForView(new DisplayView(), viewModel, "display-view");

        var slider = session.Window.GetVisualDescendants().OfType<Slider>()
            .First(s => ReferenceEquals(s.DataContext, viewModel.Monitors[1]));

        var mid = slider.Bounds.Height / 2;
        var from = slider.TranslatePoint(new Point(slider.Bounds.Width * 0.5, mid), session.Window)!.Value;
        var to = slider.TranslatePoint(new Point(slider.Bounds.Width * 0.9, mid), session.Window)!.Value;

        session.Window.MouseMove(from);
        session.Window.MouseDown(from, MouseButton.Left);
        session.Pump();
        session.Window.MouseMove(to);
        session.Pump();
        session.Screenshot("display-slider-pressed");
        session.Window.MouseUp(to, MouseButton.Left);
        session.Pump();

        Assert.True(viewModel.Monitors[1].Brightness > 70,
            $"drag did not move the slider; value is {viewModel.Monitors[1].Brightness}");
        for (var i = 0; i < 200 && monitors.Writes.Count == 0; i++)
        {
            session.Pump();
            Thread.Sleep(10);
        }

        Assert.Contains(monitors.Writes, w => w.StartsWith(@"brightness:\\.\DISPLAY2|0="));
    }

    [AvaloniaFact]
    public void ScrollingOverTheSlider_StepsTheValue()
    {
        var monitors = new StubMonitorService();
        var viewModel = new DisplayViewModel(SampleData(), monitors, new UiFakePowerService());
        using var session = UiSession.ForView(new DisplayView(), viewModel, "display-view");

        var slider = session.Window.GetVisualDescendants().OfType<Slider>()
            .First(s => ReferenceEquals(s.DataContext, viewModel.Monitors[1]));
        var point = slider.TranslatePoint(
            new Point(slider.Bounds.Width / 2, slider.Bounds.Height / 2), session.Window)!.Value;

        session.Window.MouseWheel(point, new Vector(0, 1));
        session.Pump();

        Assert.Equal(60, viewModel.Monitors[1].Brightness); // 55 + one 5-step notch
    }

    [AvaloniaFact]
    public void TypingAnExactValue_AppliesOnEnter()
    {
        var monitors = new StubMonitorService();
        var viewModel = new DisplayViewModel(SampleData(), monitors, new UiFakePowerService());
        using var session = UiSession.ForView(new DisplayView(), viewModel, "display-view");

        var box = session.Window.GetVisualDescendants().OfType<TextBox>()
            .First(t => ReferenceEquals(t.DataContext, viewModel.Monitors[1]));

        session.Click(box);
        box.SelectAll();
        session.Window.KeyTextInput("62");
        session.Pump();
        session.Window.KeyPressQwerty(PhysicalKey.Enter, RawInputModifiers.None);
        session.Pump();

        Assert.Equal(62, viewModel.Monitors[1].Brightness);
        for (var i = 0; i < 200 && monitors.Writes.Count == 0; i++)
        {
            session.Pump();
            Thread.Sleep(10);
        }

        Assert.Contains(monitors.Writes, w => w == @"brightness:\\.\DISPLAY2|0=62");
    }

    [AvaloniaFact]
    public void LinkedBrightness_MovesEveryDdcMonitorToTheSameFraction()
    {
        var monitors = new StubMonitorService();
        var viewModel = new DisplayViewModel(SampleData(), monitors, new UiFakePowerService());
        using var session = UiSession.ForView(new DisplayView(), viewModel, "display-view");

        Assert.True(viewModel.CanLinkBrightness); // built-in + VG27AQ both dimmable
        viewModel.LinkBrightness = true;
        session.Screenshot("display-link-toggle");

        viewModel.Monitors[1].Brightness = 40;

        // The DDC-less "Older monitor" must stay untouched; the built-in panel
        // follows to the same fraction of its range (both are 0-100 here).
        Assert.Equal(40, viewModel.Monitors[0].Brightness);

        for (var i = 0; i < 200 && monitors.Writes.Count < 2; i++)
        {
            session.Pump();
            Thread.Sleep(10);
        }

        Assert.Contains(monitors.Writes, w => w == @"brightness:\\.\DISPLAY2|0=40");
    }

    [AvaloniaFact]
    public void UnlinkedBrightness_LeavesOtherMonitorsAlone()
    {
        var monitors = new StubMonitorService();
        var viewModel = new DisplayViewModel(SampleData(), monitors, new UiFakePowerService());
        using var session = UiSession.ForView(new DisplayView(), viewModel, "display-view");

        viewModel.Monitors[1].Brightness = 40;
        Assert.Equal(70, viewModel.Monitors[0].Brightness);
    }

    [AvaloniaFact]
    public void ScreenOffButton_WritesPowerModeD6()
    {
        var monitors = new StubMonitorService();
        var viewModel = new DisplayViewModel(SampleData(), monitors, new UiFakePowerService());
        using var session = UiSession.ForView(new DisplayView(), viewModel, "display-view");

        var external = viewModel.Monitors[1];
        Assert.True(external.CanTurnOffScreen);
        Assert.False(viewModel.Monitors[0].CanTurnOffScreen); // internal panel: never
        Assert.False(viewModel.Monitors[2].CanTurnOffScreen); // no declared 0xD6

        external.TurnOffScreenCommand.Execute(null);
        for (var i = 0; i < 200 && !monitors.Writes.Any(w => w.StartsWith("vcp")); i++)
        {
            session.Pump();
            Thread.Sleep(10);
        }

        Assert.Contains(monitors.Writes, w => w == @"vcp:\\.\DISPLAY2|0:0xD6=4");
    }
}
