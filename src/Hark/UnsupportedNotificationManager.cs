namespace Hark;

/// <summary>Stands in where notifications cannot be shown, carrying the reason.</summary>
internal sealed class UnsupportedNotificationManager(string reason) : INotificationManager
{
    public bool IsSupported => false;
    public string? UnsupportedReason { get; } = reason;
    public NotificationCapabilities Capabilities => NotificationCapabilities.None;

    public Task<NotificationHandle> ShowAsync(Notification notification, CancellationToken cancellationToken = default)
        => throw new InvalidOperationException($"Notifications are not supported: {UnsupportedReason}");

    public Task CloseAsync(NotificationHandle handle, CancellationToken cancellationToken = default) => Task.CompletedTask;

    // Never raised; explicit no-op accessors keep the compiler from warning about unused events.
    public event EventHandler<NotificationActivatedEventArgs>? Activated { add { } remove { } }
    public event EventHandler<NotificationClosedEventArgs>? Closed { add { } remove { } }

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;
}
