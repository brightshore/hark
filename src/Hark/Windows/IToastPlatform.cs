namespace Hark.Windows;

/// <summary>
/// The WinRT toast surface Hark uses, and nothing else. Every decision, the toast XML included,
/// lives in <see cref="WindowsNotificationManager"/>, which talks only to this, so it is testable
/// on any OS. <see cref="ToastPlatform"/> is the one real implementation.
/// </summary>
internal interface IToastPlatform
{
    /// <summary>Shows a toast, replacing any on screen with the same tag.</summary>
    void Show(ToastRequest request);

    /// <summary>Takes a toast off screen and out of Notification Center. Unknown tags are ignored.</summary>
    void Hide(string tag);

    /// <summary>Where toast events go, on an OS thread.</summary>
    void SetSink(IToastEventSink sink);

    /// <summary>Why clicks after the app exits will not reach it; null when the COM activator is registered.</summary>
    string? ActivatorUnavailableReason { get; }
}

/// <summary>Receives a toast's events, identified by its tag.</summary>
internal interface IToastEventSink
{
    /// <summary>
    /// A click. <paramref name="arguments"/> carries the toast's tag (see <see cref="ToastArguments"/>).
    /// <paramref name="fromActivator"/> is true when it came through the COM activator, which only
    /// ever calls for this app's own toasts, including ones shown before the process started.
    /// </summary>
    void OnActivated(string arguments, bool fromActivator);

    /// <summary><paramref name="reason"/> is <c>ToastDismissalReason</c>: 0 user, 1 app hid it, 2 timed out to Notification Center.</summary>
    void OnDismissed(string tag, int reason);

    void OnFailed(string tag, int errorCode);
}

/// <summary>A toast: its tag (unique per app, at most 64 characters), its XML, and whether to skip the popup.</summary>
internal sealed record ToastRequest(string Tag, string Xml, bool SuppressPopup);
