using System.Runtime.Versioning;
using System.Security.Cryptography;
using System.Text;
using System.Xml.Linq;

namespace Hark.Windows;

/// <summary>
/// Notifications as Windows toasts, Windows 10 1809+.
/// </summary>
/// <remarks>
/// <para>
/// <see cref="AppIdentity.AppId"/> becomes the AppUserModelID and <see cref="AppIdentity.DisplayName"/>
/// the name Windows shows on each toast. <see cref="AppIdentity.IconPath"/> is the toast's app icon.
/// </para>
/// <para>
/// <see cref="Notification.Expiration"/> is given to Windows as the toast's own expiry, so the toast
/// goes even if the app has exited, and a timer removes it and closes it as
/// <see cref="NotificationCloseReason.Expired"/> while the app runs, since Windows reports nothing.
/// How long the popup itself stays up is only "short" or "long"; anything over
/// <see cref="ShortPopup"/>, or never, asks for long.
/// </para>
/// <para>
/// Not available on Windows: <see cref="NotificationActivatedEventArgs.ActivationToken"/>.
/// </para>
/// </remarks>
internal sealed class WindowsNotificationManager : INotificationManager, IToastEventSink
{
    /// <summary>About how long Windows shows a "short" popup.</summary>
    internal static readonly TimeSpan ShortPopup = TimeSpan.FromSeconds(7);

    /// <summary>Windows caps a toast tag at 64 characters.</summary>
    internal const int MaxTagLength = 64;

    private const NotificationCapabilities WindowsCapabilities =
        NotificationCapabilities.Show | NotificationCapabilities.Actions | NotificationCapabilities.BodyActivation
        | NotificationCapabilities.Images | NotificationCapabilities.Sound | NotificationCapabilities.Persistence
        | NotificationCapabilities.Close;

    private readonly IToastPlatform _platform;
    private readonly TimeProvider _time;
    private readonly ExpiryTimers _expiry;
    private readonly Lock _gate = new();
    private readonly Dictionary<string, NotificationHandle> _live = new(StringComparer.Ordinal);
    private readonly List<NotificationActivatedEventArgs> _pendingActivations = [];
    private EventHandler<NotificationActivatedEventArgs>? _activated;
    private bool _disposed;

    private WindowsNotificationManager(IToastPlatform platform, TimeProvider time)
    {
        _platform = platform;
        _time = time;
        _expiry = new ExpiryTimers(time);
    }

    public bool IsSupported => true;
    public string? UnsupportedReason => null;
    public NotificationCapabilities Capabilities => WindowsCapabilities;

    /// <summary>Why clicks after the app exits will not reach it; null when they will.</summary>
    internal string? ActivatorUnavailableReason => _platform.ActivatorUnavailableReason;

    /// <summary>
    /// Activations that arrive before anyone subscribes are held and delivered to the first
    /// subscriber: an app started by a toast click receives that click from the activator during
    /// startup, before it has had a chance to wire this event.
    /// </summary>
    public event EventHandler<NotificationActivatedEventArgs>? Activated
    {
        add
        {
            NotificationActivatedEventArgs[] pending;
            lock (_gate)
            {
                _activated += value;
                pending = [.. _pendingActivations];
                _pendingActivations.Clear();
            }
            foreach (var activation in pending)
                value?.Invoke(this, activation);
        }
        remove
        {
            lock (_gate)
                _activated -= value;
        }
    }

    public event EventHandler<NotificationClosedEventArgs>? Closed;

    [SupportedOSPlatform("windows")]
    public static INotificationManager Create(AppIdentity identity)
    {
        var platform = ToastPlatform.TryCreate(identity, out var reason);
        return platform is null ? new UnsupportedNotificationManager(reason!) : Create(platform);
    }

    /// <summary>The seam the tests use.</summary>
    internal static WindowsNotificationManager Create(IToastPlatform platform, TimeProvider? time = null)
    {
        var manager = new WindowsNotificationManager(platform, time ?? TimeProvider.System);
        platform.SetSink(manager);
        return manager;
    }

    public Task<NotificationHandle> ShowAsync(Notification notification, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(notification);
        ObjectDisposedException.ThrowIf(_disposed, this);
        NotificationValidation.Validate(notification);
        cancellationToken.ThrowIfCancellationRequested();

        // Every toast gets a tag, because a tag is what Hide and History address. A caller's tag
        // maps to the same toast tag every time, which is what makes Windows replace in place.
        var toastTag = ToastTag(notification.Tag);
        var expires = notification.Expiration is { } e && e != Timeout.InfiniteTimeSpan ? e : (TimeSpan?)null;
        _platform.Show(new ToastRequest(
            toastTag,
            BuildXml(notification, toastTag),
            SuppressPopup: notification.Urgency == NotificationUrgency.Low,
            ExpiresAt: expires is { } after ? _time.GetUtcNow() + after : null));

        var handle = new NotificationHandle(toastTag, notification.Tag);
        lock (_gate)
            _live[toastTag] = handle;

        // A replacement without an expiry must not inherit its predecessor's.
        if (expires is { } expiry)
            _expiry.Schedule(toastTag, expiry, () => Expire(handle));
        else
            _expiry.Cancel(toastTag);
        return Task.FromResult(handle);
    }

    public Task CloseAsync(NotificationHandle handle, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(handle);
        ObjectDisposedException.ThrowIf(_disposed, this);

        _platform.Hide(handle.Id);
        _expiry.Cancel(handle.Id);
        bool removed;
        lock (_gate)
            removed = _live.Remove(handle.Id);
        if (removed)
            Closed?.Invoke(this, new NotificationClosedEventArgs(handle, NotificationCloseReason.ClosedByApp));
        return Task.CompletedTask;
    }

    internal static string ToastTag(string? tag)
    {
        if (tag is null)
            return "g:" + Guid.NewGuid().ToString("N");
        if (tag.Length <= MaxTagLength - 2)
            return "t:" + tag;
        // Too long to use as is: a hash keeps it stable, so replacement still works.
        return "h:" + Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(tag)))[..40];
    }

    /// <summary>
    /// The toast, in Windows' adaptive toast schema. Built as XML, never by string concatenation,
    /// so a title or body can hold any text.
    /// </summary>
    internal static string BuildXml(Notification notification, string toastTag)
    {
        var binding = new XElement("binding", new XAttribute("template", "ToastGeneric"),
            new XElement("text", notification.Title));
        if (!string.IsNullOrEmpty(notification.Body))
            binding.Add(new XElement("text", notification.Body));
        if (notification.ImagePath is { } imagePath)
            binding.Add(new XElement("image", new XAttribute("src", new Uri(Path.GetFullPath(imagePath)).AbsoluteUri)));

        var toast = new XElement("toast",
            new XAttribute("launch", ToastArguments.Format(toastTag, actionId: null)),
            new XElement("visual", binding));

        // "urgent" keeps the toast up and lets it through Focus on Windows 11; older Windows ignores it.
        if (notification.Urgency == NotificationUrgency.Critical)
            toast.Add(new XAttribute("scenario", "urgent"));

        if (notification.Actions.Count > 0)
        {
            toast.Add(new XElement("actions", notification.Actions.Select(action =>
                new XElement("action",
                    new XAttribute("content", action.Label),
                    new XAttribute("arguments", ToastArguments.Format(toastTag, action.Id)),
                    new XAttribute("activationType", "foreground")))));
        }

        if (notification.Silent)
            toast.Add(new XElement("audio", new XAttribute("silent", "true")));

        if (notification.Expiration is { } expiration && (expiration == Timeout.InfiniteTimeSpan || expiration > ShortPopup))
            toast.Add(new XAttribute("duration", "long"));

        return toast.ToString(SaveOptions.DisableFormatting);
    }

    void IToastEventSink.OnActivated(string arguments, bool fromActivator)
    {
        // Not Hark's arguments: not a toast this library made.
        if (ToastArguments.Parse(arguments) is not { } parsed)
            return;

        NotificationActivatedEventArgs activation;
        EventHandler<NotificationActivatedEventArgs>? handlers;
        lock (_gate)
        {
            if (!_live.Remove(parsed.ToastTag, out var handle))
            {
                // The in-process event only reports toasts this process showed. The activator also
                // reports ones an earlier run showed; this process has no handle for those, so it
                // makes the one that run would have had.
                if (!fromActivator)
                    return;
                handle = new NotificationHandle(parsed.ToastTag, OriginalTag(parsed.ToastTag));
            }
            activation = new NotificationActivatedEventArgs(handle, parsed.ActionId, activationToken: null);
            _expiry.Cancel(parsed.ToastTag);
            handlers = _activated;
            if (handlers is null)
                _pendingActivations.Add(activation);
        }
        handlers?.Invoke(this, activation);
    }

    /// <summary>The caller's tag behind a toast tag, where it can be recovered (a hashed one cannot).</summary>
    internal static string? OriginalTag(string toastTag) => toastTag.StartsWith("t:", StringComparison.Ordinal) ? toastTag[2..] : null;

    void IToastEventSink.OnDismissed(string tag, int reason)
    {
        // Timed out (2): the toast has only moved to Notification Center and can still be clicked.
        if (reason == 2)
            return;
        Close(tag, reason == 0 ? NotificationCloseReason.Dismissed : NotificationCloseReason.ClosedByApp);
    }

    void IToastEventSink.OnFailed(string tag, int errorCode) => Close(tag, NotificationCloseReason.Unknown);

    private void Expire(NotificationHandle handle)
    {
        _platform.Hide(handle.Id);
        Close(handle.Id, NotificationCloseReason.Expired);
    }

    private void Close(string tag, NotificationCloseReason reason)
    {
        _expiry.Cancel(tag);
        NotificationHandle? handle;
        lock (_gate)
        {
            if (!_live.Remove(tag, out handle))
                return;
        }
        Closed?.Invoke(this, new NotificationClosedEventArgs(handle, reason));
    }

    public ValueTask DisposeAsync()
    {
        _disposed = true;
        _expiry.Dispose();
        return ValueTask.CompletedTask;
    }
}
