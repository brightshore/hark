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
    private EventHandler<NotificationActivatedEventArgs>? _forwardActivated;
    private EventHandler<NotificationClosedEventArgs>? _forwardClosed;

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
        _forwardActivated = (_, e) => Dispatcher.UIThread.Post(() => _activated?.Invoke(this, e));
        _forwardClosed = (_, e) => Dispatcher.UIThread.Post(() => _closed?.Invoke(this, e));
        _inner.Activated += _forwardActivated;
        _inner.Closed += _forwardClosed;
    }

    /// <summary>Stops forwarding, then disposes the inner manager: nothing reaches this wrapper's handlers after.</summary>
    public ValueTask DisposeAsync()
    {
        lock (_gate)
        {
            if (_forwardActivated is not null) _inner.Activated -= _forwardActivated;
            if (_forwardClosed is not null) _inner.Closed -= _forwardClosed;
            _forwardActivated = null;
            _forwardClosed = null;
            _activated = null;
            _closed = null;
        }
        return _inner.DisposeAsync();
    }
}
