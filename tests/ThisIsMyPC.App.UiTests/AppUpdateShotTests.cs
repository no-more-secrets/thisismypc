using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.VisualTree;
using Avalonia.Headless.XUnit;
using Avalonia.Styling;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using ThisIsMyPC.App.ViewModels;
using ThisIsMyPC.Core.Results;
using ThisIsMyPC.Core.Services;
using ThisIsMyPC.Core.Settings;

namespace ThisIsMyPC.App.UiTests;

public class AppUpdateShotTests
{
    private sealed class FakeUpdater : IUpdateService
    {
        public TaskCompletionSource<bool> Download { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public int Restarts { get; private set; }
        public int Downloads { get; private set; }
        public Task<OperationResult<UpdateCheckResult>> CheckForUpdateAsync() =>
            Task.FromResult(OperationResult<UpdateCheckResult>.Success(new UpdateCheckResult(true, "2.0.0", null)));
        public async Task<OperationResult<bool>> DownloadUpdateAsync(IProgress<int>? progress = null, CancellationToken cancellationToken = default)
        {
            Downloads++;
            progress?.Report(45);
            return OperationResult<bool>.Success(await Download.Task.WaitAsync(cancellationToken));
        }
        public void ApplyUpdateAndRestart() => Restarts++;
    }

    [AvaloniaFact(Timeout = 60_000)]
    [Trait("Category", "Diagnostic")]
    public async Task HeaderOffersDownloadThenExplicitRestartAndLogonChoicesPersist()
    {
        var updater = new FakeUpdater();
        using var session = UiSession.ForMainWindow("annotation-settings", services =>
        {
            services.RemoveAll<IUpdateService>();
            services.AddSingleton<IUpdateService>(updater);
        });
        session.Window.Width = 1200;
        session.Window.Height = 800;
        var vm = (MainWindowViewModel)session.Window.DataContext!;
        await session.WaitForAsync(() => vm.IsUpdateBadgeVisible, what: "available update");
        session.ClickText("Settings");
        var settings = session.Services!.GetRequiredService<ISettingsService>();
        foreach (var theme in new[] { ThemeVariant.Dark, ThemeVariant.Light })
        {
            session.SetTheme(theme);
            session.Screenshot($"download-{theme.Key}");
            var button = session.Find<Button>(b => b.Name == "AppUpdateButton");
            Assert.InRange(session.TopOf(button), 64, 105);
        }
        var logon = session.Find<ComboBox>(b => b.DataContext is SettingChoiceItemViewModel { DisplayName: "Start with Windows" });
        session.Click(logon);
        session.Screenshot("logon-options");
        session.ClickText("Open at logon");
        Assert.Equal("2", settings.GetApp(AppSettingKeys.AutoStart, "0"));
        session.Pump();
        Assert.False(logon.IsDropDownOpen, "Logon dropdown did not close");
        Assert.True(vm.DownloadOrRestartUpdateCommand.CanExecute(null));
        Assert.True(session.Find<Button>(b => b.Name == "AppUpdateButton").IsEffectivelyEnabled);
        var downloadButton = session.Find<Button>(b => b.Name == "AppUpdateButton");
        var hit = session.Window.InputHitTest(session.CenterOf(downloadButton));
        Assert.True(hit == downloadButton || (hit is Avalonia.Visual visual && visual.GetVisualAncestors().Contains(downloadButton)),
            $"Hit {hit?.GetType().Name} instead of update button at {session.CenterOf(downloadButton)}; bounds {downloadButton.Bounds}. Ancestors: {string.Join(", ", ((Avalonia.Visual)hit!).GetVisualAncestors().OfType<Control>().Select(c => $"{c.GetType().Name}:{c.Name}:{string.Join(".", c.Classes)}"))}");
        session.Screenshot("before-download-click");
        session.Click(downloadButton);
        Assert.Equal(1, updater.Downloads);
        await session.WaitForAsync(() => vm.IsUpdateDownloading && vm.UpdateDownloadProgress == 45, what: "download progress");
        Assert.False(session.Find<Button>(b => b.Name == "AppUpdateButton").IsEffectivelyEnabled, "Download button stays enabled during download");
        session.Screenshot("downloading");
        updater.Download.SetResult(true);
        await session.WaitForAsync(() => vm.IsUpdateReady, what: "verified download");
        Assert.Equal(0, updater.Restarts);
        foreach (var theme in new[] { ThemeVariant.Dark, ThemeVariant.Light })
        {
            session.SetTheme(theme);
            session.Screenshot($"ready-{theme.Key}");
        }
        session.ClickText("Restart ThisIsMyPC");
        Assert.Equal(1, updater.Restarts);
        var page = (SettingsViewModel)vm.CurrentContent!;
        var check = page.ApplicationSection.Items.OfType<SettingToggleItemViewModel>().Single(t => t.DisplayName == "Check for app updates");
        var toggle = session.Find<ToggleSwitch>(t => t.DataContext == check);
        session.Click(toggle);
        var automatic = session.Find<ToggleSwitch>(t => t.DataContext is SettingToggleItemViewModel { DisplayName: "Automatically download updates" });
        Assert.False(automatic.IsEffectivelyEnabled, "Automatic download option stays enabled with checks off");
        session.Screenshot("automatic-download-disabled");
    }

    [AvaloniaTheory(Timeout = 60_000)]
    [InlineData(false)]
    [InlineData(true)]
    [Trait("Category", "Diagnostic")]
    public async Task DisablingChecksCancelsAutomaticDownloadWithoutInstalling(bool enableAgain)
    {
        var updater = new FakeUpdater();
        using var session = UiSession.ForMainWindow("annotation-update-cancel", services =>
        {
            services.RemoveAll<IUpdateService>();
            services.AddSingleton<IUpdateService>(updater);
        });
        var vm = (MainWindowViewModel)session.Window.DataContext!;
        await session.WaitForAsync(() => vm.IsUpdateBadgeVisible, what: "available update");
        var settings = session.Services!.GetRequiredService<ISettingsService>();
        settings.SetApp(AppSettingKeys.AutoDownloadUpdates, "1");
        await session.WaitForAsync(() => vm.IsUpdateDownloading, what: "automatic download");
        settings.SetApp(AppSettingKeys.UpdateCheck, "0");
        if (enableAgain)
        {
            settings.SetApp(AppSettingKeys.UpdateCheck, "1");
            await session.WaitForAsync(() => updater.Downloads == 2, what: "resumed automatic download");
            updater.Download.SetResult(true);
            await session.WaitForAsync(() => vm.IsUpdateReady, what: "resumed download completed");
            Assert.Equal(0, updater.Restarts);
            return;
        }
        await session.WaitForAsync(() => !vm.IsUpdateDownloading, what: "cancelled download");
        Assert.False(vm.IsUpdateReady);
        Assert.Equal(0, updater.Restarts);
        Assert.Equal("Download update", vm.UpdateActionText);
    }
}
