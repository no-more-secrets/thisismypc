using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Styling;
using Avalonia.VisualTree;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using ThisIsMyPC.App.Controls;
using ThisIsMyPC.App.UiTests.Fakes;
using ThisIsMyPC.App.ViewModels;
using ThisIsMyPC.App.Views;
using ThisIsMyPC.Core.Results;
using ThisIsMyPC.Core.Services;
using ThisIsMyPC.Modules.Shell.Models;

namespace ThisIsMyPC.App.UiTests;

public class RestartAnnotationShotTests
{
    [AvaloniaFact]
    public async Task ToastAction_DisablesWhileRunning_AndDoesNotDismissItself()
    {
        var completion = new TaskCompletionSource();
        var command = new AsyncRelayCommand(() => completion.Task);
        var stack = new ToastStackViewModel(TimeSpan.Zero);
        stack.Show("May require an Explorer restart", "Open File Explorer windows will close.",
            ToastSeverity.Warning, sticky: true, actionLabel: "Restart Explorer", actionCommand: command);
        using var session = UiSession.ForView(new ToastHostControl(), stack, "restart-annotations", width: 420, height: 240);
        session.Screenshot("restart-action-dark");
        session.SetTheme(ThemeVariant.Light);
        session.Screenshot("restart-action-light");
        var button = session.Find<Button>(b => Equals(b.Content, "Restart Explorer"));
        session.Click(button);
        Assert.True(command.IsRunning);
        Assert.False(button.IsEffectivelyEnabled);
        completion.SetResult();
        await session.WaitForAsync(() => !command.IsRunning, what: "restart action");
        Assert.Single(stack.Toasts);
        Assert.True(button.IsEffectivelyEnabled);
    }

    [AvaloniaFact]
    public async Task LegacyPowerShell_IsGreyAndCannotStage_ButOtherStaticVerbsCan()
    {
        var pending = new PendingChangesService();
        using var vm = new ContextMenuViewModel([Verb("PowerShell"), Verb("Custom Terminal")], pending, new UiFakeRegistryService());
        using var session = UiSession.ForView(new ContextMenuView(), vm, "restart-annotations", width: 1000, height: 540);
        session.ClickText(vm.DesktopHandlerCount);
        var row = Assert.Single(vm.DesktopHandlers, h => h.Label == "PowerShell");
        Assert.False(row.IsToggleEnabled);
        Assert.True(row.IsInactive);
        Assert.Contains("Currently non-functional", row.InactiveReason);
        session.Screenshot("powershell-disabled-dark");
        session.SetTheme(ThemeVariant.Light);
        session.Screenshot("powershell-disabled-light");
        row.IsEnabled = !row.IsEnabled;
        await Task.Delay(350);
        session.Pump();
        Assert.Equal(0, pending.PendingCount);
        var custom = Assert.Single(vm.DesktopHandlers, h => h.Label == "Custom Terminal");
        Assert.True(custom.IsToggleEnabled);
        custom.IsEnabled = !custom.IsEnabled;
        await session.WaitForAsync(() => pending.PendingCount == 1, what: "custom verb staging");
    }

    private static ContextMenuHandler Verb(string name) => new(
        name, "", @"HKCR\DesktopBackground\Shell\" + name, "Desktop background", null, null, true,
        HandlerClassification.System, HandlerType: HandlerType.StaticVerb,
        VerbInfo: new StaticVerbInfo(name, null, null, null, false, "powershell.exe", null, false, null, false, false));

    [AvaloniaFact]
    [Trait("Category", "Diagnostic")]
    public async Task MainWindow_RestartAction_ClearsNotice_AndToastInsetsMatch()
    {
        var restart = new RestartService();
        using var session = UiSession.ForMainWindow("restart-mainwindow", services =>
        {
            services.RemoveAll<IExplorerRestartService>();
            services.AddSingleton<IExplorerRestartService>(restart);
        });
        var vm = (MainWindowViewModel)session.Window.DataContext!;
        session.OpenModule("Context Menus");
        await session.WaitForAsync(() => vm.CurrentContent is ContextMenuViewModel, what: "context menus");
        vm.IsRestartNotificationVisible = true;
        vm.IsRestartActionAvailable = true;
        vm.ToastStack.Show("May require an Explorer restart", "Open File Explorer windows will close.",
            ToastSeverity.Warning, sticky: true, key: "restart-notice",
            actionLabel: "Restart Explorer", actionCommand: vm.RestartExplorerCommand);
        session.Screenshot("context-toast");
        var host = session.Find<Border>(b => b.Name == "ModuleContentHost");
        var toast = session.Find<ToastHostControl>(_ => true);
        var hostOrigin = host.TranslatePoint(default, session.Window)!.Value;
        var toastOrigin = toast.TranslatePoint(default, session.Window)!.Value;
        var rightGap = hostOrigin.X + host.Bounds.Width - toastOrigin.X - toast.Bounds.Width;
        var topGap = toastOrigin.Y - hostOrigin.Y;
        Assert.InRange(Math.Abs(rightGap - topGap), 0, 1);
        Assert.InRange(rightGap, 10, 14);
        var notice = Assert.Single(vm.ToastStack.Toasts);
        Assert.True(notice.IsActionVisible);
        session.OpenModule("Explorer");
        await session.WaitForAsync(() => vm.CurrentContent is ShellViewModel, what: "Explorer page");
        Assert.False(notice.IsActionVisible);
        var toastButton = toast.GetVisualDescendants().OfType<Button>()
            .Single(b => Equals(b.Content, "Restart Explorer"));
        Assert.False(toastButton.IsVisible);
        var headerButton = session.Find<Button>(b => b.Name == "RestartExplorerButton");
        Assert.True(headerButton.IsVisible);
        session.Screenshot("explorer-header-dark");
        var explorerHostOrigin = host.TranslatePoint(default, session.Window)!.Value;
        Assert.InRange(Math.Abs(toastOrigin.Y - explorerHostOrigin.Y - rightGap), 0, 1);
        session.SetTheme(ThemeVariant.Light);
        session.Screenshot("explorer-header-light");
        session.OpenModule("Context Menus");
        await session.WaitForAsync(() => vm.CurrentContent is ContextMenuViewModel, what: "return to context menus");
        Assert.True(notice.IsActionVisible);
        Assert.True(toastButton.IsVisible);
        vm.StatusMessage = "Changes applied. May require an Explorer restart";
        vm.StatusSeverity = StatusSeverity.Warning;
        session.Screenshot("context-toast-light");
        session.SetTheme(ThemeVariant.Dark);
        session.Screenshot("dismissible-status-dark");
        var barHeight = session.Find<Border>(b => b.Name == "ApplyBar").Bounds.Height;
        session.Click(session.Find<Button>(b => b.Name == "DismissStatusButton"));
        Assert.Empty(vm.StatusMessage);
        Assert.Same(notice, Assert.Single(vm.ToastStack.Toasts));
        Assert.Equal(barHeight, session.Find<Border>(b => b.Name == "ApplyBar").Bounds.Height);
        vm.StatusMessage = "Another message";
        session.Pump();
        session.Click(session.Find<Button>(b => b.Name == "DismissStatusButton"));
        Assert.Empty(vm.StatusMessage);
        session.ClickText("Restart Explorer");
        await session.WaitForAsync(() => restart.Called && !vm.IsRestartingExplorer, what: "fake Explorer restart");
        Assert.Empty(vm.ToastStack.Toasts);
        Assert.Empty(vm.StatusMessage);
        session.Screenshot("after-restart");
    }

    private sealed class RestartService : IExplorerRestartService
    {
        public bool Called { get; private set; }
        public Task<OperationResult<bool>> RestartExplorerAsync()
        {
            Called = true;
            return Task.FromResult(OperationResult<bool>.Success(true));
        }
        public Task<OperationResult<bool>> RefreshExplorerViewsAsync() => Task.FromResult(OperationResult<bool>.Success(true));
    }
}
