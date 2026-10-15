using System.Runtime.Versioning;

namespace Hark.MacOS;

/// <summary>
/// Notifications through <c>UNUserNotificationCenter</c>, macOS 11+.
/// </summary>
/// <remarks>
/// <para>
/// macOS identifies the app by its bundle, so <see cref="AppIdentity.AppId"/> is not used here: the
/// bundle's <c>CFBundleIdentifier</c> is. The app must run from a signed <c>.app</c> bundle (ad-hoc
/// signing is enough for local runs); anywhere else the manager reports unsupported.
/// </para>
/// <para>
/// macOS has no per-notification timeout, so <see cref="Notification.Expiration"/> is a timer here:
/// when it fires the notification is removed from screen and Notification Center and closes as
/// <see cref="NotificationCloseReason.Expired"/>. It only runs while the app does. How long a banner
/// stays up is the user's choice of style, so a never-expiring notification is simply left alone.
/// </para>
/// </remarks>
internal sealed class MacNotificationManager : INotificationManager, IMacResponseSink
{
    internal const string DefaultActionIdentifier = "com.apple.UNNotificationDefaultActionIdentifier";
    internal const string DismissActionIdentifier = "com.apple.UNNotificationDismissActionIdentifier";
    internal const string TagIdentifierPrefix = "tag:";

    private const NotificationCapabilities MacCapabilities =
        NotificationCapabilities.Show | NotificationCapabilities.Actions | NotificationCapabilities.BodyActivation
        | NotificationCapabilities.Images | NotificationCapabilities.Sound | NotificationCapabilities.Persistence
        | NotificationCapabilities.Close;

    private readonly IUserNotificationCenter _center;
    private readonly ExpiryTimers _expiry;
    private readonly Lock _gate = new();
    private readonly Dictionary<string, NotificationHandle> _live = new(StringComparer.Ordinal);
    private readonly Dictionary<string, MacCategory> _categories = new(StringComparer.Ordinal);
    private bool _disposed;

    private MacNotificationManager(IUserNotificationCenter center, TimeProvider time)
    {
        _center = center;
        _expiry = new ExpiryTimers(time);
    }

    public bool IsSupported => true;
    public string? UnsupportedReason => null;
    public NotificationCapabilities Capabilities => MacCapabilities;

    public event EventHandler<NotificationActivatedEventArgs>? Activated;
    public event EventHandler<NotificationClosedEventArgs>? Closed;

    [SupportedOSPlatform("macos")]
    public static Task<INotificationManager> CreateAsync(CancellationToken cancellationToken)
    {
        var center = UserNotificationCenter.TryCreate(out var reason);
        return center is null
            ? Task.FromResult<INotificationManager>(new UnsupportedNotificationManager(reason!))
            : CreateAsync(center, cancellationToken);
    }

    /// <summary>The seam the tests use.</summary>
    internal static async Task<INotificationManager> CreateAsync(IUserNotificationCenter center, CancellationToken cancellationToken, TimeProvider? time = null)
    {
        var manager = new MacNotificationManager(center, time ?? TimeProvider.System);
        center.SetResponseSink(manager);

        var (granted, error) = await center.RequestAuthorizationAsync().WaitAsync(cancellationToken).ConfigureAwait(false);
        if (!granted)
        {
            // Both cases are fixed in the same place. macOS remembers a refusal per bundle id and
            // never prompts again, so the app cannot recover on its own.
            const string Fix = "Turn on Allow Notifications for this app in System Settings > Notifications, then restart it.";
            return new UnsupportedNotificationManager(error is null
                ? $"Notifications are turned off for this app. {Fix}"
                : $"macOS refused notification permission: {error}. {Fix}");
        }
        return manager;
    }

    public async Task<NotificationHandle> ShowAsync(Notification notification, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(notification);
        ObjectDisposedException.ThrowIf(_disposed, this);
        NotificationValidation.Validate(notification);

        // A tag is the request identifier itself: macOS replaces a delivered request with the same
        // identifier in place, which is exactly the tag contract. The prefix keeps tags and generated
        // ids from ever colliding.
        var identifier = notification.Tag is { } tag ? TagIdentifierPrefix + tag : Guid.NewGuid().ToString("N");
        var category = EnsureCategory(notification.Actions);

        await _center.AddAsync(new MacNotificationRequest(
            Identifier: identifier,
            Title: notification.Title,
            Body: notification.Body ?? "",
            CategoryIdentifier: category,
            ImagePath: notification.ImagePath,
            PlaySound: !notification.Silent,
            InterruptionLevel: notification.Urgency switch
            {
                NotificationUrgency.Low => MacInterruptionLevel.Passive,
                // Time-sensitive breaks through Focus only with the entitlement; without it, it is
                // still the strongest level an ordinary app can ask for. Critical needs Apple's approval.
                NotificationUrgency.Critical => MacInterruptionLevel.TimeSensitive,
                _ => MacInterruptionLevel.Active,
            })).WaitAsync(cancellationToken).ConfigureAwait(false);

        var handle = new NotificationHandle(identifier, notification.Tag);
        lock (_gate)
            _live[identifier] = handle;

        // A replacement without an expiry must not inherit its predecessor's.
        if (notification.Expiration is { } expiration && expiration != Timeout.InfiniteTimeSpan)
            _expiry.Schedule(identifier, expiration, () => Expire(handle));
        else
            _expiry.Cancel(identifier);
        return handle;
    }

    public Task CloseAsync(NotificationHandle handle, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(handle);
        ObjectDisposedException.ThrowIf(_disposed, this);

        _center.Remove(handle.Id);
        _expiry.Cancel(handle.Id);

        // macOS reports nothing for a removal it was asked to make. Linux does, so for the same
        // contract on both, the manager reports it.
        bool removed;
        lock (_gate)
            removed = _live.Remove(handle.Id);
        if (removed)
            Closed?.Invoke(this, new NotificationClosedEventArgs(handle, NotificationCloseReason.ClosedByApp));
        return Task.CompletedTask;
    }

    /// <summary>
    /// Buttons belong to categories, registered up front. Each distinct button set gets one,
    /// named by its contents, and the whole set is re-registered when a new one appears.
    /// </summary>
    private string EnsureCategory(IReadOnlyList<NotificationAction> actions)
    {
        var identifier = CategoryIdentifier(actions);
        MacCategory[]? all = null;
        lock (_gate)
        {
            if (!_categories.ContainsKey(identifier))
            {
                _categories[identifier] = new MacCategory(identifier, [.. actions]);
                all = [.. _categories.Values];
            }
        }
        if (all is not null)
            _center.SetCategories(all);
        return identifier;
    }

    /// <summary>Unit and record separators cannot appear in a label anyone types, so ids never collide.</summary>
    internal static string CategoryIdentifier(IReadOnlyList<NotificationAction> actions)
        => "hark:" + string.Join('\u001e', actions.Select(a => a.Id + '\u001f' + a.Label));

    void IMacResponseSink.OnResponse(string requestIdentifier, string actionIdentifier)
    {
        NotificationHandle? handle;
        lock (_gate)
        {
            if (!_live.TryGetValue(requestIdentifier, out handle))
                return;
            // Any response ends the notification on macOS: a click or a button removes it too.
            _live.Remove(requestIdentifier);
        }
        _expiry.Cancel(requestIdentifier);

        if (actionIdentifier == DismissActionIdentifier)
            Closed?.Invoke(this, new NotificationClosedEventArgs(handle, NotificationCloseReason.Dismissed));
        else
            Activated?.Invoke(this, new NotificationActivatedEventArgs(
                handle, actionIdentifier == DefaultActionIdentifier ? null : actionIdentifier, activationToken: null));
    }

    private void Expire(NotificationHandle handle)
    {
        _center.Remove(handle.Id);
        bool removed;
        lock (_gate)
            removed = _live.Remove(handle.Id);
        if (removed)
            Closed?.Invoke(this, new NotificationClosedEventArgs(handle, NotificationCloseReason.Expired));
    }

    public ValueTask DisposeAsync()
    {
        _disposed = true;
        _expiry.Dispose();
        return ValueTask.CompletedTask;
    }
}
