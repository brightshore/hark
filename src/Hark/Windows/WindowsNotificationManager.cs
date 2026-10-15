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
/// Not available on Windows: <see cref="Notification.Expiration"/> (a toast's on-screen time is the
/// user's setting) and <see cref="NotificationActivatedEventArgs.ActivationToken"/>.
/// </para>
/// </remarks>
internal sealed class WindowsNotificationManager : INotificationManager, IToastEventSink
{
    internal const string DefaultArguments = "hark:default";
    internal const string ActionArgumentsPrefix = "hark:action:";

    /// <summary>Windows caps a toast tag at 64 characters.</summary>
    internal const int MaxTagLength = 64;

    private const NotificationCapabilities WindowsCapabilities =
        NotificationCapabilities.Show | NotificationCapabilities.Actions | NotificationCapabilities.BodyActivation
        | NotificationCapabilities.Images | NotificationCapabilities.Sound | NotificationCapabilities.Persistence
        | NotificationCapabilities.Close;

    private readonly IToastPlatform _platform;
    private readonly Lock _gate = new();
    private readonly Dictionary<string, NotificationHandle> _live = new(StringComparer.Ordinal);
    private bool _disposed;

    private WindowsNotificationManager(IToastPlatform platform) => _platform = platform;

    public bool IsSupported => true;
    public string? UnsupportedReason => null;
    public NotificationCapabilities Capabilities => WindowsCapabilities;

    public event EventHandler<NotificationActivatedEventArgs>? Activated;
    public event EventHandler<NotificationClosedEventArgs>? Closed;

    [SupportedOSPlatform("windows")]
    public static INotificationManager Create(AppIdentity identity)
    {
        var platform = ToastPlatform.TryCreate(identity, out var reason);
        return platform is null ? new UnsupportedNotificationManager(reason!) : Create(platform);
    }

    /// <summary>The seam the tests use.</summary>
    internal static WindowsNotificationManager Create(IToastPlatform platform)
    {
        var manager = new WindowsNotificationManager(platform);
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
        _platform.Show(new ToastRequest(toastTag, BuildXml(notification), SuppressPopup: notification.Urgency == NotificationUrgency.Low));

        var handle = new NotificationHandle(toastTag, notification.Tag);
        lock (_gate)
            _live[toastTag] = handle;
        return Task.FromResult(handle);
    }

    public Task CloseAsync(NotificationHandle handle, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(handle);
        ObjectDisposedException.ThrowIf(_disposed, this);

        _platform.Hide(handle.Id);
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
    internal static string BuildXml(Notification notification)
    {
        var binding = new XElement("binding", new XAttribute("template", "ToastGeneric"),
            new XElement("text", notification.Title));
        if (!string.IsNullOrEmpty(notification.Body))
            binding.Add(new XElement("text", notification.Body));
        if (notification.ImagePath is { } imagePath)
            binding.Add(new XElement("image", new XAttribute("src", new Uri(Path.GetFullPath(imagePath)).AbsoluteUri)));

        var toast = new XElement("toast",
            new XAttribute("launch", DefaultArguments),
            new XElement("visual", binding));

        // "urgent" keeps the toast up and lets it through Focus on Windows 11; older Windows ignores it.
        if (notification.Urgency == NotificationUrgency.Critical)
            toast.Add(new XAttribute("scenario", "urgent"));

        if (notification.Actions.Count > 0)
        {
            toast.Add(new XElement("actions", notification.Actions.Select(action =>
                new XElement("action",
                    new XAttribute("content", action.Label),
                    new XAttribute("arguments", ActionArgumentsPrefix + action.Id),
                    new XAttribute("activationType", "foreground")))));
        }

        if (notification.Silent)
            toast.Add(new XElement("audio", new XAttribute("silent", "true")));

        return toast.ToString(SaveOptions.DisableFormatting);
    }

    void IToastEventSink.OnActivated(string tag, string arguments)
    {
        // Arguments that are not Hark's (a malformed or foreign toast) are a body click.
        var actionId = arguments.StartsWith(ActionArgumentsPrefix, StringComparison.Ordinal)
            ? arguments[ActionArgumentsPrefix.Length..]
            : null;

        NotificationHandle? handle;
        lock (_gate)
        {
            if (!_live.Remove(tag, out handle))
                return;
        }
        Activated?.Invoke(this, new NotificationActivatedEventArgs(handle, actionId, activationToken: null));
    }

    void IToastEventSink.OnDismissed(string tag, int reason)
    {
        // Timed out (2): the toast has only moved to Notification Center and can still be clicked.
        if (reason == 2)
            return;
        Close(tag, reason == 0 ? NotificationCloseReason.Dismissed : NotificationCloseReason.ClosedByApp);
    }

    void IToastEventSink.OnFailed(string tag, int errorCode) => Close(tag, NotificationCloseReason.Unknown);

    private void Close(string tag, NotificationCloseReason reason)
    {
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
        return ValueTask.CompletedTask;
    }
}
