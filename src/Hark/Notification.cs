namespace Hark;

/// <summary>A notification to show. Immutable; build one per <see cref="INotificationManager.ShowAsync"/> call.</summary>
public sealed record Notification
{
    /// <summary>The headline. Required.</summary>
    public required string Title { get; init; }

    /// <summary>Plain text body. Never interpreted as markup: Hark escapes it where the platform would.</summary>
    public string? Body { get; init; }

    /// <summary>Absolute path to an image shown with the notification.</summary>
    public string? ImagePath { get; init; }

    /// <summary>Buttons. Check <see cref="NotificationCapabilities.Actions"/>: servers without it drop them.</summary>
    public IReadOnlyList<NotificationAction> Actions { get; init; } = [];

    /// <summary>How urgently the platform should present it.</summary>
    public NotificationUrgency Urgency { get; init; } = NotificationUrgency.Normal;

    /// <summary>Suppress the notification sound.</summary>
    public bool Silent { get; init; }

    /// <summary>
    /// How long it stays up. <c>null</c> leaves it to the platform; <see cref="Timeout.InfiniteTimeSpan"/>
    /// asks it never to expire. Platforms are free to ignore this.
    /// </summary>
    public TimeSpan? Expiration { get; init; }

    /// <summary>
    /// Showing a notification with the same tag as one still on screen replaces it in place
    /// instead of stacking a second one.
    /// </summary>
    public string? Tag { get; init; }
}

/// <summary>A button on a notification.</summary>
/// <param name="Id">Reported back in <see cref="NotificationActivatedEventArgs.ActionId"/>.</param>
/// <param name="Label">The button text.</param>
public sealed record NotificationAction(string Id, string Label);

/// <summary>Presentation urgency.</summary>
public enum NotificationUrgency
{
    /// <summary>Background information.</summary>
    Low,
    /// <summary>The default.</summary>
    Normal,
    /// <summary>Needs attention; platforms may keep it on screen until dismissed.</summary>
    Critical,
}
