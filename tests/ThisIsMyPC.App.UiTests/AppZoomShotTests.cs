using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Styling;
using Avalonia.VisualTree;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using ThisIsMyPC.App.ViewModels;
using ThisIsMyPC.Core.Settings;

namespace ThisIsMyPC.App.UiTests;

public class AppZoomShotTests
{
    [AvaloniaFact(Timeout = 300_000)]
    [Trait("Category", "Diagnostic")]
    public async Task ZoomKeysReflowTheWholeApp_KeepCaptionsVisible_AndPersist()
    {
        var settingsPath = Path.Combine(Path.GetTempPath(), $"tipc-zoom-{Guid.NewGuid():N}.json");
        var settings = new SettingsService(settingsPath);
        settings.Initialize();
        using var session = UiSession.ForMainWindow("app-zoom", services =>
        {
            services.RemoveAll<ISettingsService>();
            services.AddSingleton<ISettingsService>(settings);
        });
        var vm = (MainWindowViewModel)session.Window.DataContext!;
        await session.WaitForAsync(() => vm.SidebarGroups.Count > 0, what: "sidebar");
        session.ClickText("Explorer");
        await session.WaitForAsync(() => vm.CurrentContent is ShellViewModel, what: "Explorer");
        session.ClickText("Taskbar");
        var page = vm.CurrentContent;
        var search = session.Find<TextBox>(b => b.Watermark == "Search settings...");
        search.Focus();
        session.Window.KeyPressQwerty(PhysicalKey.Equal, RawInputModifiers.Control);
        session.Pump();
        Assert.Equal(110, vm.ZoomPercent);
        Assert.Same(page, vm.CurrentContent);
        Assert.Equal(2, ((ITabbedPage)page!).SelectedTabIndex);
        Assert.True(string.IsNullOrEmpty(search.Text));
        foreach (var theme in new[] { ThemeVariant.Dark, ThemeVariant.Light })
        foreach (var zoom in new[] { 50, 100, 150 })
        {
            session.SetTheme(theme);
            session.Window.Width = 900;
            session.Window.Height = 600;
            session.Window.KeyPressQwerty(PhysicalKey.Digit0, RawInputModifiers.Control);
            for (var i = 0; i < Math.Abs(zoom - 100) / 10; i++)
                session.Window.KeyPressQwerty(zoom > 100 ? PhysicalKey.Equal : PhysicalKey.Minus, RawInputModifiers.Control);
            session.Pump();
            Assert.Equal(zoom, vm.ZoomPercent);
            var surface = session.Find<Grid>(g => g.Name == "AppSurface");
            Assert.Equal(session.Window.ClientSize.Width / vm.UiScale, surface.Bounds.Width, 1);
            var close = session.Find<Button>(b => AutomationProperties.GetName(b) == "Close");
            var edge = close.TranslatePoint(new Point(close.Bounds.Width, close.Bounds.Height), session.Window)!.Value;
            Assert.InRange(edge.X, session.Window.ClientSize.Width - 2, session.Window.ClientSize.Width + 1);
            session.Screenshot($"{theme.Key}-{zoom}");
        }
        vm.UpdateBadgeText = "Update 1.0.1";
        vm.IsUpdateBadgeVisible = true;
        session.Click(session.Find<ToggleSwitch>(_ => true));
        await session.WaitForAsync(() => vm.HasPendingChanges, what: "pending change");
        session.Pump();
        var footer = session.Find<Border>(b => b.Name == "ApplyBar");
        var footerButtons = footer.GetVisualDescendants().OfType<Button>().Where(b => b.IsEffectivelyVisible).ToArray();
        foreach (var button in footerButtons)
        {
            var origin = button.TranslatePoint(default, footer)!.Value;
            Assert.InRange(origin.X + button.Bounds.Width, 0, footer.Bounds.Width);
        }
        session.Screenshot("max-zoom-pending-update");
        vm.DiscardAllCommand.Execute(null);
        vm.IsUpdateBadgeVisible = false;
        session.Window.KeyPressQwerty(PhysicalKey.Equal, RawInputModifiers.Control);
        Assert.Equal(150, vm.ZoomPercent);
        var restored = new SettingsService(settingsPath);
        restored.Initialize();
        Assert.Equal("150", restored.GetApp(AppSettingKeys.UiZoom, "100"));
        var combo = session.Find<ComboBox>(_ => true);
        combo.BringIntoView();
        session.Pump();
        session.Click(combo);
        session.Pump();
        var popup = combo.GetVisualDescendants().OfType<Popup>().Single();
        Assert.True(popup.IsOpen);
        Assert.True(popup.InheritsTransform);
        var popupHost = popup.Child!.GetVisualAncestors().OfType<IPopupHost>().First();
        Assert.Equal(1.5, popupHost.Transform!.Value.M11, 0.01);
        combo.IsDropDownOpen = false;
        session.Window.KeyPressQwerty(PhysicalKey.Digit0, RawInputModifiers.Control);
        Assert.Equal(100, vm.ZoomPercent);
#if DEBUG
        var window = (ThisIsMyPC.App.Views.MainWindow)session.Window;
        var reviewPath = Path.Combine(session.ShotDirectory, Guid.NewGuid().ToString("N"));
        window.StartRegionReview(reviewPath);
        session.Window.MouseDown(new Point(250, 250), MouseButton.Left);
        session.Window.MouseMove(new Point(400, 350));
        session.Window.MouseUp(new Point(400, 350), MouseButton.Left);
        session.Window.KeyPressQwerty(PhysicalKey.Equal, RawInputModifiers.Control);
        session.Pump();
        session.Screenshot("annotation-after-zoom");
        Assert.Equal(110, vm.ZoomPercent);
        Assert.False(window.RegionReviewOverlay!.IsReviewActive);
        window.StartRegionReview(reviewPath);
        session.Pump();
        session.Window.MouseDown(new Point(450, 250), MouseButton.Left);
        session.Window.MouseMove(new Point(550, 350));
        session.Window.MouseUp(new Point(550, 350), MouseButton.Left);
        using var record = System.Text.Json.JsonDocument.Parse(File.ReadAllText(Path.Combine(reviewPath, "latest.json")));
        Assert.Contains(record.RootElement.GetProperty("captures").EnumerateArray(),
            c => c.GetProperty("layoutState").GetString()!.Contains("zoom=110", StringComparison.Ordinal));
        window.RegionReviewOverlay.Suspend();
#endif
    }
}
