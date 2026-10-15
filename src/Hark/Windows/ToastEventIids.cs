namespace Hark.Windows;

/// <summary>
/// The IIDs of the three toast event-handler delegates. WinRT derives these from the delegate's
/// type signature rather than assigning them; WinRTIidTests recomputes each one. Plain data, so
/// it carries no platform restriction and the check runs on every OS.
/// </summary>
internal static class ToastEventIids
{
    /// <summary>TypedEventHandler&lt;ToastNotification, Object&gt;: the Activated event.</summary>
    public static readonly Guid Activated = new("ab54de2d-97d9-5528-b6ad-105afe156530");

    /// <summary>TypedEventHandler&lt;ToastNotification, ToastDismissedEventArgs&gt;.</summary>
    public static readonly Guid Dismissed = new("61c2402f-0ed0-5a18-ab69-59f4aa99a368");

    /// <summary>TypedEventHandler&lt;ToastNotification, ToastFailedEventArgs&gt;.</summary>
    public static readonly Guid Failed = new("95e3e803-c969-5e3a-9753-ea2ad22a9a33");
}
