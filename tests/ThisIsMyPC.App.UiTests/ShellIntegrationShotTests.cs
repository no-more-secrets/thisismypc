using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Styling;
using ThisIsMyPC.App.UiTests.Fakes;
using ThisIsMyPC.App.ViewModels;
using ThisIsMyPC.App.Views;
using ThisIsMyPC.Core.Results;
using ThisIsMyPC.Core.Services;
using ThisIsMyPC.Modules.Shell.Changes;
using ThisIsMyPC.Modules.Shell.Models;
using ThisIsMyPC.Modules.Shell.Services;

namespace ThisIsMyPC.App.UiTests;

public class ShellIntegrationShotTests
{
    [AvaloniaFact]
    [Trait("Category", "Diagnostic")]
    public async Task ExplorerAnnotationsOnLiveReadOnlyScan()
    {
        using var session = UiSession.ForMainWindow("explorer-annotations-live");
        var main = (MainWindowViewModel)session.Window.DataContext!;
        session.OpenModule("Explorer");
        await session.WaitForAsync(() => main.CurrentContent is ShellViewModel, what: "Explorer scan");
        foreach (var tab in new[] { "File Explorer", "Start Menu", "Desktop" })
        {
            session.ClickText(tab);
            session.Screenshot(tab.Replace(' ', '-'));
        }
        var power = session.Find<ComboBox>(b => b.DataContext is ShellChoiceSettingViewModel r
            && r.Label.StartsWith("Alt+F4", StringComparison.Ordinal));
        session.Click(power);
        session.Screenshot("power-options");
    }

    [AvaloniaFact]
    public async Task AnnotatedPagesShowMissingControlsAndExplainDisabledRows()
    {
        var registry = new UiFakeRegistryService();
        var scan = new ShellScanData([], new(1, true, false, false),
            new ExplorerPatcherSettingsReader(registry, 26100, _ => false, () => (false, false, false)).ReadAll(), true)
            { TaskbarAutoHideState = "2", RoundedCornersState = "absent" };
        var queue = new PendingChangesService();
        using var vm = new ShellViewModel(scan, queue, registry, nativeSettings: new FakeNative());
        var card = new Border { Margin = new Thickness(16), BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(8), Child = new ShellView() };
        card.Bind(Border.BackgroundProperty, card.GetResourceObservable("RaisedBrush"));
        card.Bind(Border.BorderBrushProperty, card.GetResourceObservable("OutlineBrush"));
        using var session = UiSession.ForView(card, vm, "shell-integration", width: 980, height: 900);
        foreach (var theme in new[] { ThemeVariant.Dark, ThemeVariant.Light })
        {
            session.SetTheme(theme);
            foreach (var tab in new[] { "General", "File Explorer", "Taskbar", "Start Menu", "Desktop" })
            {
                session.ClickText(tab);
                session.Screenshot(theme.Key + "-" + tab.Replace(' ', '-'));
            }
            var power = session.Find<ComboBox>(b => b.DataContext is ShellChoiceSettingViewModel r && r.Label.StartsWith("Alt+F4", StringComparison.Ordinal));
            session.Click(power);
            session.Screenshot(theme.Key + "-power-options");
            power.IsDropDownOpen = false;
        }
        var navigation = vm.PatcherToggles.Single(r => r.Label == "Disable navigation bar");
        Assert.False(navigation.IsToggleEnabled);
        Assert.Equal("Requires shell-extension registration.", navigation.InactiveReason);
        Assert.DoesNotContain(vm.GeneralPatcherGroups.SelectMany(g => g.Toggles), r => r.Label.StartsWith("Register ExplorerPatcher", StringComparison.Ordinal));
        Assert.StartsWith("Register ExplorerPatcher", vm.FileExplorerPatcherGroups.First(g => g.Heading.Length == 0).Toggles.First().Label);
        navigation.IsEnabled = true;
        Assert.False(navigation.IsEnabled);
        var style = vm.PatcherChoices.Single(r => r.Label == "Start menu style");
        Assert.False(style.IsControlEnabled);
        Assert.Contains("files", style.PolicyStateText);
        Assert.Contains(vm.PatcherToggles, r => r.Label == "Disable rounded corners for application windows");
        Assert.Contains(vm.PatcherToggles, r => r.Label == "Register ExplorerPatcher as a shell extension");
        Assert.Empty(queue.PendingGroups);
        session.ClickText("Taskbar");
        var autoHide = vm.TaskbarSettings.Single(r => r.Label == "Automatically hide the taskbar");
        autoHide.IsEnabled = true;
        await session.WaitForAsync(() => queue.PendingCount == 1, timeoutMs: 5000, what: "auto-hide staging");
        var change = Assert.Single(Assert.Single(queue.PendingGroups).Changes);
        Assert.Equal("2", change.BeforeValue);
        Assert.Equal("3", change.AfterValue);
        session.Screenshot("auto-hide-staged");
        session.Window.Width = 440;
        session.ClickText("File Explorer");
        session.Screenshot("narrow-registration-required");
        using var revisited = new ShellViewModel(scan, queue, registry, nativeSettings: new FakeNative());
        var restored = revisited.TaskbarSettings.Single(r => r.Label == "Automatically hide the taskbar");
        Assert.True(restored.IsEnabled);
        Assert.True(restored.HasPendingChange);
        restored.IsEnabled = false;
        await session.WaitForAsync(() => queue.PendingCount == 0, timeoutMs: 5000, what: "remove restored staging");
    }

    private sealed class FakeNative : IShellNativeSettings
    {
        public OperationResult<string> ReadTaskbarState() => OperationResult<string>.Success("2");
        public OperationResult<string> ReadRoundedCornersState() => OperationResult<string>.Success("absent");
        public OperationResult<bool> WriteTaskbarState(string state) => throw new NotSupportedException();
        public Task<OperationResult<bool>> WriteRoundedCornersStateAsync(string state) => throw new NotSupportedException();
    }
}
