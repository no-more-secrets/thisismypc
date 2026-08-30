namespace ThisIsMyPC.Core.Notifications;

public enum NotificationType
{
    /// <summary>9-3 monitoring detections (new startup entry/service).</summary>
    Monitoring,

    /// <summary>7-3 update available.</summary>
    UpdateAvailable,
}

public sealed record AppNotification(NotificationType Type, string Title, string Message);

/// <summary>
/// Opt-in notifications (9-2). Implementations gate on the master toggle plus the
/// per-type toggle; when gated off, nothing is raised anywhere; the information is
/// only visible inside the app. Windows toast rendering is deferred to the UI/UX
/// chapter; today the App surfaces raised notifications through its own chrome.
/// </summary>
public interface INotificationService
{
    /// <summary>True when the notification passed the gates and was raised.</summary>
    bool Notify(NotificationType type, string title, string message);

    event EventHandler<AppNotification>? NotificationRaised;
}
