using ThisIsMyPC.Core.Settings;

namespace ThisIsMyPC.Core.Notifications;

public sealed class NotificationService : INotificationService
{
    private readonly ISettingsService _settings;

    public NotificationService(ISettingsService settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        _settings = settings;
    }

    public event EventHandler<AppNotification>? NotificationRaised;

    public bool Notify(NotificationType type, string title, string message)
    {
        if (!_settings.GetAppBool(AppSettingKeys.Notifications, fallback: true))
            return false;

        var typeKey = type switch
        {
            NotificationType.Monitoring => AppSettingKeys.NotifyMonitoring,
            NotificationType.UpdateAvailable => AppSettingKeys.NotifyUpdates,
            _ => null,
        };
        if (typeKey is not null && !_settings.GetAppBool(typeKey, fallback: true))
            return false;

        NotificationRaised?.Invoke(this, new AppNotification(type, title, message));
        return true;
    }
}
