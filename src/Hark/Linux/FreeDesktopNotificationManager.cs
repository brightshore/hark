using System.Globalization;
using Tmds.DBus.Protocol;

namespace Hark.Linux;

/// <summary>
/// Notifications through the freedesktop.org Desktop Notifications Specification
/// (<c>org.freedesktop.Notifications</c> on the session bus), which GNOME, KDE, dunst, mako,
/// swaync and the rest all implement.
/// </summary>
internal sealed class FreeDesktopNotificationManager : INotificationManager, INotificationSignalSink
{
    /// <summary>The spec's reserved action key for clicking the notification body.</summary>
    internal const string DefaultActionKey = "default";

    /// <summary>
    /// The default action needs a label on the wire. Most servers never display it (clicking the body
    /// is the gesture); the few that render it as a button get a sensible word.
    /// </summary>
    internal const string DefaultActionLabel = "Open";

    private readonly AppIdentity _identity;
    private readonly INotificationsBus _bus;
    private readonly Lock _gate = new();
    private readonly Dictionary<uint, NotificationHandle> _live = [];
    private readonly Dictionary<string, uint> _idsByTag = [];
    private readonly Dictionary<uint, string> _pendingTokens = [];
    private bool _bodyMarkup;
    private bool _disposed;

    private FreeDesktopNotificationManager(AppIdentity identity, INotificationsBus bus)
    {
        _identity = identity;
        _bus = bus;
    }

    public bool IsSupported => true;
    public string? UnsupportedReason => null;
    public NotificationCapabilities Capabilities { get; private set; }

    public event EventHandler<NotificationActivatedEventArgs>? Activated;
    public event EventHandler<NotificationClosedEventArgs>? Closed;

    /// <summary>Connects to the real session bus. Unavailability becomes an unsupported manager, not an exception.</summary>
    public static async Task<INotificationManager> CreateAsync(AppIdentity identity, CancellationToken cancellationToken)
    {
        var address = DBusAddress.Session;
        if (string.IsNullOrEmpty(address))
            return new UnsupportedNotificationManager("No D-Bus session bus: DBUS_SESSION_BUS_ADDRESS is not set.");

        DBusNotificationsBus? bus = null;
        try
        {
            bus = await DBusNotificationsBus.ConnectAsync(address, cancellationToken).ConfigureAwait(false);
            return await CreateAsync(identity, bus, cancellationToken).ConfigureAwait(false);
        }
        catch (DBusExceptionBase ex)
        {
            if (bus is not null)
                await bus.DisposeAsync().ConfigureAwait(false);
            return new UnsupportedNotificationManager($"The session bus has no usable notification service: {ex.Message}");
        }
    }

    /// <summary>The seam the tests use: any <see cref="INotificationsBus"/>, which the manager then owns.</summary>
    internal static async Task<FreeDesktopNotificationManager> CreateAsync(AppIdentity identity, INotificationsBus bus, CancellationToken cancellationToken)
    {
        var manager = new FreeDesktopNotificationManager(identity, bus);

        // Subscribe before anything is shown, so no signal about one of our notifications can go unobserved.
        await bus.SubscribeAsync(manager).WaitAsync(cancellationToken).ConfigureAwait(false);

        var serverCapabilities = await bus.GetCapabilitiesAsync().WaitAsync(cancellationToken).ConfigureAwait(false);
        manager.Capabilities = MapCapabilities(serverCapabilities);
        manager._bodyMarkup = serverCapabilities.Contains("body-markup");
        return manager;
    }

    internal static NotificationCapabilities MapCapabilities(IReadOnlyCollection<string> server)
    {
        // Show and Close are mandatory in the spec; everything else is the daemon's choice.
        var caps = NotificationCapabilities.Show | NotificationCapabilities.Close;
        if (server.Contains("actions"))
            caps |= NotificationCapabilities.Actions | NotificationCapabilities.BodyActivation;
        if (server.Contains("body-images") || server.Contains("icon-static"))
            caps |= NotificationCapabilities.Images;
        if (server.Contains("sound"))
            caps |= NotificationCapabilities.Sound;
        if (server.Contains("persistence"))
            caps |= NotificationCapabilities.Persistence;
        return caps;
    }

    public async Task<NotificationHandle> ShowAsync(Notification notification, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(notification);
        ObjectDisposedException.ThrowIf(_disposed, this);
        Validate(notification);

        uint replacesId = 0;
        if (notification.Tag is { } tag)
        {
            lock (_gate)
                _idsByTag.TryGetValue(tag, out replacesId);
        }

        var request = BuildRequest(notification, replacesId);
        var id = await _bus.NotifyAsync(request).WaitAsync(cancellationToken).ConfigureAwait(false);

        // A signal for this id can race the reply's continuation. One that lands first is dropped by
        // the unknown-id check; for a notification that closed before Notify even returned, that is
        // the honest outcome anyway.
        var handle = new NotificationHandle(id.ToString(CultureInfo.InvariantCulture), notification.Tag);
        lock (_gate)
        {
            _live[id] = handle;
            if (notification.Tag is { } shownTag)
                _idsByTag[shownTag] = id;
        }
        return handle;
    }

    public async Task CloseAsync(NotificationHandle handle, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(handle);
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (!uint.TryParse(handle.Id, NumberStyles.None, CultureInfo.InvariantCulture, out var id))
            throw new ArgumentException($"'{handle.Id}' is not a notification id this manager issued.", nameof(handle));

        try
        {
            await _bus.CloseNotificationAsync(id).WaitAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (DBusErrorReplyException)
        {
            // The spec answers an unknown id with an error reply. The contract is "no-op if already gone".
        }
    }

    internal NotifyRequest BuildRequest(Notification notification, uint replacesId)
    {
        string[] actions = [];
        if (Capabilities.HasFlag(NotificationCapabilities.Actions))
        {
            // Flat [key, label, key, label, ...]; "default" first so a body click is always reported.
            actions = new string[(notification.Actions.Count + 1) * 2];
            actions[0] = DefaultActionKey;
            actions[1] = DefaultActionLabel;
            for (var i = 0; i < notification.Actions.Count; i++)
            {
                actions[(i + 1) * 2] = notification.Actions[i].Id;
                actions[(i + 1) * 2 + 1] = notification.Actions[i].Label;
            }
        }

        var body = notification.Body ?? "";
        if (_bodyMarkup)
            body = EscapeMarkup(body);

        var hints = new NotifyHints(
            Urgency: notification.Urgency switch
            {
                NotificationUrgency.Low => 0,
                NotificationUrgency.Critical => 2,
                _ => 1,
            },
            DesktopEntry: _identity.AppId,
            ImagePath: notification.ImagePath,
            SuppressSound: notification.Silent);

        return new NotifyRequest(
            AppName: _identity.DisplayName,
            ReplacesId: replacesId,
            AppIcon: _identity.IconPath ?? "",
            Summary: notification.Title,
            Body: body,
            Actions: actions,
            Hints: hints,
            ExpireTimeout: ToExpireTimeout(notification.Expiration));
    }

    /// <summary>Spec: -1 is the server's default, 0 is never, anything else is milliseconds.</summary>
    internal static int ToExpireTimeout(TimeSpan? expiration)
    {
        if (expiration is not { } value)
            return -1;
        if (value == Timeout.InfiniteTimeSpan)
            return 0;
        if (value <= TimeSpan.Zero)
            throw new ArgumentOutOfRangeException(nameof(expiration), value, "Expiration must be positive, or Timeout.InfiniteTimeSpan for never.");
        // A sub-millisecond positive value must not round down to 0, which means "never".
        return (int)Math.Clamp(Math.Ceiling(value.TotalMilliseconds), 1, int.MaxValue);
    }

    /// <summary>
    /// Servers advertising <c>body-markup</c> parse the body as a small HTML subset, so a literal
    /// <c>&lt;</c> or <c>&amp;</c> would be eaten or break the whole body. The body is plain text by contract.
    /// Only the three XML-significant characters are escaped: quote entities are not in every server's
    /// subset, and outside a tag they need no escaping.
    /// </summary>
    internal static string EscapeMarkup(string text)
        => text.Replace("&", "&amp;", StringComparison.Ordinal)
               .Replace("<", "&lt;", StringComparison.Ordinal)
               .Replace(">", "&gt;", StringComparison.Ordinal);

    private static void Validate(Notification notification)
    {
        if (string.IsNullOrWhiteSpace(notification.Title))
            throw new ArgumentException("A notification needs a title.", nameof(notification));
        _ = ToExpireTimeout(notification.Expiration);

        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var action in notification.Actions)
        {
            if (string.IsNullOrEmpty(action.Id))
                throw new ArgumentException("An action needs a non-empty id.", nameof(notification));
            if (action.Id == DefaultActionKey)
                throw new ArgumentException($"The action id '{DefaultActionKey}' is reserved for clicking the notification body.", nameof(notification));
            if (!seen.Add(action.Id))
                throw new ArgumentException($"The action id '{action.Id}' is used twice.", nameof(notification));
        }
    }

    void INotificationSignalSink.OnActivationToken(uint id, string activationToken)
    {
        lock (_gate)
        {
            if (_live.ContainsKey(id))
                _pendingTokens[id] = activationToken;
        }
    }

    void INotificationSignalSink.OnActionInvoked(uint id, string actionKey)
    {
        NotificationHandle? handle;
        string? token;
        lock (_gate)
        {
            if (!_live.TryGetValue(id, out handle))
                return;
            _pendingTokens.Remove(id, out token);
        }
        Activated?.Invoke(this, new NotificationActivatedEventArgs(handle, actionKey == DefaultActionKey ? null : actionKey, token));
    }

    void INotificationSignalSink.OnNotificationClosed(uint id, uint reason)
    {
        NotificationHandle? handle;
        lock (_gate)
        {
            if (!_live.Remove(id, out handle))
                return;
            _pendingTokens.Remove(id);
            if (handle.Tag is { } tag && _idsByTag.TryGetValue(tag, out var tagged) && tagged == id)
                _idsByTag.Remove(tag);
        }
        Closed?.Invoke(this, new NotificationClosedEventArgs(handle, reason switch
        {
            1 => NotificationCloseReason.Expired,
            2 => NotificationCloseReason.Dismissed,
            3 => NotificationCloseReason.ClosedByApp,
            _ => NotificationCloseReason.Unknown,
        }));
    }

    public async ValueTask DisposeAsync()
    {
        if (_disposed)
            return;
        _disposed = true;
        await _bus.DisposeAsync().ConfigureAwait(false);
    }
}
