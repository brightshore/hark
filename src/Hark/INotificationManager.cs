namespace Hark;

/// <summary>
/// Shows native OS notifications and reports what the user did with them.
/// Get one from <see cref="NotificationManager.CreateAsync"/>.
/// </summary>
/// <remarks>
/// Events are raised on whatever thread the platform delivers them on, never a UI thread.
/// Hark.Avalonia wraps a manager so they arrive on Avalonia's dispatcher.
/// </remarks>
public interface INotificationManager : IAsyncDisposable
{
    /// <summary>False when this platform, or this machine, cannot show notifications. See <see cref="UnsupportedReason"/>.</summary>
    bool IsSupported { get; }

    /// <summary>Why <see cref="IsSupported"/> is false; <c>null</c> when it is true.</summary>
    string? UnsupportedReason { get; }

    /// <summary>What the platform's notification service honours.</summary>
    NotificationCapabilities Capabilities { get; }

    /// <summary>Shows a notification.</summary>
    /// <exception cref="InvalidOperationException">The manager is not supported; the message carries the reason.</exception>
    /// <exception cref="ArgumentException">The notification is invalid, such as an action using a reserved id.</exception>
    Task<NotificationHandle> ShowAsync(Notification notification, CancellationToken cancellationToken = default);

    /// <summary>Removes a notification from the screen. A no-op if it is already gone.</summary>
    Task CloseAsync(NotificationHandle handle, CancellationToken cancellationToken = default);

    /// <summary>The user clicked the notification or one of its buttons.</summary>
    event EventHandler<NotificationActivatedEventArgs>? Activated;

    /// <summary>A notification left the screen, for any reason.</summary>
    event EventHandler<NotificationClosedEventArgs>? Closed;
}

/// <summary>Identifies a shown notification. Compare by value.</summary>
/// <param name="Id">Platform-assigned and opaque. A replacement via <see cref="Notification.Tag"/> keeps the id.</param>
/// <param name="Tag">The tag it was shown with, if any.</param>
public sealed record NotificationHandle(string Id, string? Tag);

/// <summary>Raised when the user activates a notification.</summary>
public sealed class NotificationActivatedEventArgs(NotificationHandle handle, string? actionId, string? activationToken) : EventArgs
{
    /// <summary>The notification.</summary>
    public NotificationHandle Handle { get; } = handle;

    /// <summary>The <see cref="NotificationAction.Id"/> of the button, or <c>null</c> when the body was clicked.</summary>
    public string? ActionId { get; } = actionId;

    /// <summary>
    /// A platform token for raising the app's window, when the platform supplies one (the XDG
    /// activation token on Wayland). Without it, a compositor may refuse to focus the window.
    /// </summary>
    public string? ActivationToken { get; } = activationToken;
}

/// <summary>Raised when a notification leaves the screen.</summary>
public sealed class NotificationClosedEventArgs(NotificationHandle handle, NotificationCloseReason reason) : EventArgs
{
    /// <summary>The notification.</summary>
    public NotificationHandle Handle { get; } = handle;

    /// <summary>Why it closed.</summary>
    public NotificationCloseReason Reason { get; } = reason;
}

/// <summary>Why a notification closed.</summary>
public enum NotificationCloseReason
{
    /// <summary>The platform did not say.</summary>
    Unknown,
    /// <summary>It timed out.</summary>
    Expired,
    /// <summary>The user dismissed it.</summary>
    Dismissed,
    /// <summary>The app closed it with <see cref="INotificationManager.CloseAsync"/>.</summary>
    ClosedByApp,
}
