namespace Hark;

/// <summary>
/// The rules every backend enforces identically, so a notification that works on one platform is
/// never rejected on another.
/// </summary>
internal static class NotificationValidation
{
    /// <summary>
    /// Reserved on every platform, not only Linux where the spec gives it meaning: an app written
    /// against one backend must not discover the clash on another.
    /// </summary>
    public const string ReservedActionId = "default";

    public static void Validate(Notification notification)
    {
        ArgumentNullException.ThrowIfNull(notification);
        if (string.IsNullOrWhiteSpace(notification.Title))
            throw new ArgumentException("A notification needs a title.", nameof(notification));
        if (notification.Expiration is { } expiration && expiration != Timeout.InfiniteTimeSpan && expiration <= TimeSpan.Zero)
            throw new ArgumentException("Expiration must be positive, or Timeout.InfiniteTimeSpan for never.", nameof(notification));
        if (notification.Tag is { Length: 0 })
            throw new ArgumentException("A tag must be null or non-empty.", nameof(notification));

        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var action in notification.Actions)
        {
            if (string.IsNullOrEmpty(action.Id))
                throw new ArgumentException("An action needs a non-empty id.", nameof(notification));
            if (action.Id == ReservedActionId)
                throw new ArgumentException($"The action id '{ReservedActionId}' is reserved for clicking the notification body.", nameof(notification));
            if (!seen.Add(action.Id))
                throw new ArgumentException($"The action id '{action.Id}' is used twice.", nameof(notification));
        }
    }
}
