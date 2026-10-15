using Avalonia.Threading;

namespace Hark.Avalonia;

/// <summary>
/// Wraps a manager so its events are raised on Avalonia's UI thread, where handlers can touch
/// controls directly. Everything else passes straight through.
/// </summary>
public sealed class DispatchingNotificationManager(INotificationManager inner) : INotificationManager
{
    private readonly INotificationManager _inner = inner ?? throw new ArgumentNullException(nameof(inner));
    private EventHandler<NotificationActivatedEventArgs>? _activated;
    private EventHandler<NotificationClosedEventArgs>? _closed;
    private readonly Lock _gate = new();
    private bool _forwarding;

    /// <inheritdoc />
    public bool IsSupported => _inner.IsSupported;

    /// <inheritdoc />
    public string? UnsupportedReason => _inner.UnsupportedReason;

    /// <inheritdoc />
    public NotificationCapabilities Capabilities => _inner.Capabilities;

    /// <inheritdoc />
    public Task<NotificationHandle> ShowAsync(Notification notification, CancellationToken cancellationToken = default)
        => _inner.ShowAsync(notification, cancellationToken);

    /// <inheritdoc />
    public Task CloseAsync(NotificationHandle handle, CancellationToken cancellationToken = default)
        => _inner.CloseAsync(handle, cancellationToken);

    /// <inheritdoc />
    public event EventHandler<NotificationActivatedEventArgs>? Activated
    {
        add { EnsureForwarding(); _activated += value; }
        remove => _activated -= value;
    }

    /// <inheritdoc />
    public event EventHandler<NotificationClosedEventArgs>? Closed
    {
        add { EnsureForwarding(); _closed += value; }
        remove => _closed -= value;
    }

    // Subscribed lazily and once, so the inner manager holds exactly one handler per event however
    // many the app adds here.
    private void EnsureForwarding()
    {
        lock (_gate)
        {
            if (_forwarding)
                return;
            _forwarding = true;
        }
        _inner.Activated += (_, e) => Dispatcher.UIThread.Post(() => _activated?.Invoke(this, e));
        _inner.Closed += (_, e) => Dispatcher.UIThread.Post(() => _closed?.Invoke(this, e));
    }

    /// <inheritdoc />
    public ValueTask DisposeAsync() => _inner.DisposeAsync();
}
