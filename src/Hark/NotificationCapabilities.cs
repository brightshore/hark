namespace Hark;

/// <summary>
/// What the platform's notification service will actually honour. Discovered at startup:
/// on Linux it depends on which notification daemon is running, not on the OS.
/// </summary>
[Flags]
public enum NotificationCapabilities
{
    /// <summary>Nothing: notifications cannot be shown.</summary>
    None = 0,
    /// <summary>Notifications can be shown.</summary>
    Show = 1 << 0,
    /// <summary>Buttons are displayed and report back.</summary>
    Actions = 1 << 1,
    /// <summary>Clicking the notification itself is reported.</summary>
    BodyActivation = 1 << 2,
    /// <summary><see cref="Notification.ImagePath"/> is displayed.</summary>
    Images = 1 << 3,
    /// <summary>The service plays sounds, so <see cref="Notification.Silent"/> means something.</summary>
    Sound = 1 << 4,
    /// <summary>Notifications stay in a history after they leave the screen.</summary>
    Persistence = 1 << 5,
    /// <summary>The app can remove a notification it showed.</summary>
    Close = 1 << 6,
}
