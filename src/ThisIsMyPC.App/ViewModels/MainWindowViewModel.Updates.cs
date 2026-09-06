using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ThisIsMyPC.Core.Notifications;
using ThisIsMyPC.Core.Settings;

namespace ThisIsMyPC.App.ViewModels;

public partial class MainWindowViewModel
{
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(DownloadOrRestartUpdateCommand))]
    private bool _isUpdateBadgeVisible;

    [ObservableProperty]
    private string _updateBadgeText = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(UpdateActionText))]
    private bool _isUpdateReady;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(UpdateActionText))]
    [NotifyCanExecuteChangedFor(nameof(DownloadOrRestartUpdateCommand))]
    private bool _isUpdateDownloading;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(UpdateActionText))]
    private int _updateDownloadProgress;

    private bool _isCheckingUpdate;
    private bool _automaticDownload;
    private CancellationTokenSource? _updateDownloadCancellation;

    public string UpdateActionText => IsUpdateDownloading
        ? $"Downloading {UpdateDownloadProgress}%"
        : IsUpdateReady ? "Restart ThisIsMyPC" : "Download update";

    private bool UpdateChecksEnabled => _settingsService?.GetAppBool(AppSettingKeys.UpdateCheck, true) ?? true;
    private bool AutomaticDownloadsEnabled => UpdateChecksEnabled
        && (_settingsService?.GetAppBool(AppSettingKeys.AutoDownloadUpdates, false) ?? false);

    private void OnUpdateSettingChanged(object? sender, SettingChangedEventArgs e)
    {
        if (e.Scope != SettingChangedEventArgs.AppScope
            || e.Key is not (AppSettingKeys.UpdateCheck or AppSettingKeys.AutoDownloadUpdates))
            return;
        void ApplyPreference()
        {
            if (!AutomaticDownloadsEnabled && _automaticDownload)
                _updateDownloadCancellation?.Cancel();
            if (!UpdateChecksEnabled)
                return;
            if (IsUpdateBadgeVisible)
            {
                if (AutomaticDownloadsEnabled && !IsUpdateReady && !IsUpdateDownloading)
                    _ = DownloadUpdateAsync(automatic: true);
            }
            else
                _ = CheckForUpdateBadgeAsync();
        }
        if (Dispatcher.UIThread.CheckAccess())
            ApplyPreference();
        else
            Dispatcher.UIThread.Post(ApplyPreference);
    }

    private async Task CheckForUpdateBadgeAsync()
    {
        if (_updateService is null || !UpdateChecksEnabled || _isCheckingUpdate || IsUpdateDownloading || IsUpdateReady)
            return;
        _isCheckingUpdate = true;
        try
        {
            var result = await _updateService.CheckForUpdateAsync().ConfigureAwait(true);
            if (!UpdateChecksEnabled)
                return;
            if (result.IsSuccess && result.Value is { IsAvailable: true, Version: { } version })
            {
                UpdateBadgeText = $"Update {version}";
                IsUpdateBadgeVisible = true;
                _notificationService?.Notify(NotificationType.UpdateAvailable,
                    "Update available", $"ThisIsMyPC {version} is available. Use Download update in the page header.");
                if (AutomaticDownloadsEnabled)
                    await DownloadUpdateAsync(automatic: true).ConfigureAwait(true);
            }
        }
#pragma warning disable CA1031 // Update failures must not interrupt app startup.
        catch (Exception ex)
        {
            Log.Warn(ex, "Update check failed");
        }
#pragma warning restore CA1031
        finally
        {
            _isCheckingUpdate = false;
            DownloadOrRestartUpdateCommand.NotifyCanExecuteChanged();
        }
    }

    private bool CanDownloadOrRestartUpdate() => _updateService is not null
        && IsUpdateBadgeVisible && !IsUpdateDownloading && !_isCheckingUpdate;

    [RelayCommand(CanExecute = nameof(CanDownloadOrRestartUpdate))]
    private async Task DownloadOrRestartUpdateAsync()
    {
        if (!CanDownloadOrRestartUpdate())
            return;
        if (!IsUpdateReady)
        {
            await DownloadUpdateAsync(automatic: false).ConfigureAwait(true);
            return;
        }
        if (HasPendingChanges || IsApplying || IsCreatingRestorePoint)
        {
            SetStatus("Finish or discard pending changes before restarting ThisIsMyPC.", StatusSeverity.Warning);
            return;
        }
        try
        {
            _updateService!.ApplyUpdateAndRestart();
        }
#pragma warning disable CA1031 // An installer failure must leave the app usable.
        catch (Exception ex)
        {
            Log.Error(ex, "Update restart failed");
            SetStatus($"Could not restart for the update: {ex.Message}", StatusSeverity.Error);
        }
#pragma warning restore CA1031
    }

    private async Task DownloadUpdateAsync(bool automatic)
    {
        if (_updateService is null || IsUpdateDownloading || IsUpdateReady || !IsUpdateBadgeVisible)
            return;
        IsUpdateDownloading = true;
        UpdateDownloadProgress = 0;
        _automaticDownload = automatic;
        using var cancellation = new CancellationTokenSource();
        _updateDownloadCancellation = cancellation;
        try
        {
            var progress = new Progress<int>(value =>
            {
                if (IsUpdateDownloading && _updateDownloadCancellation == cancellation)
                    UpdateDownloadProgress = Math.Clamp(value, 0, 100);
            });
            var result = await _updateService.DownloadUpdateAsync(progress, cancellation.Token).ConfigureAwait(true);
            if (cancellation.IsCancellationRequested)
                return;
            if (result.IsSuccess && result.Value)
            {
                IsUpdateReady = true;
                SetStatus("Update downloaded. Restart ThisIsMyPC when you are ready to install it.", StatusSeverity.Success);
            }
            else
                SetStatus(result.ErrorMessage ?? "The update could not be downloaded. Try again.", StatusSeverity.Error);
        }
        catch (OperationCanceledException) when (cancellation.IsCancellationRequested) { }
#pragma warning disable CA1031 // Download failures must leave a usable retry action.
        catch (Exception ex)
        {
            Log.Warn(ex, "Update download failed");
            SetStatus($"Update download failed: {ex.Message}", StatusSeverity.Error);
        }
#pragma warning restore CA1031
        finally
        {
            _updateDownloadCancellation = null;
            _automaticDownload = false;
            IsUpdateDownloading = false;
            // A quick off/on change can arrive before cancellation finishes.
            // Reconcile only cancelled attempts; failed downloads require a retry click.
            if (automatic && cancellation.IsCancellationRequested && AutomaticDownloadsEnabled)
                _ = DownloadUpdateAsync(automatic: true);
        }
    }

    [RelayCommand]
    private void OpenReleasesPage() => OpenUrl(Core.AppConstants.UpdateUrl, "the releases page");
}
