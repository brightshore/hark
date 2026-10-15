using Avalonia;

namespace Hark.Avalonia;

/// <summary>Avalonia setup for Hark.</summary>
public static class AvaloniaNotifications
{
    private static Task<INotificationManager>? s_manager;

    /// <summary>
    /// The app's notification manager, created by <see cref="UseNotifications"/>. Its events arrive
    /// on the UI thread. Await it where you first need it; creation connects to the platform
    /// service and is usually done long before the first notification.
    /// </summary>
    /// <exception cref="InvalidOperationException"><see cref="UseNotifications"/> was not called.</exception>
    public static Task<INotificationManager> Manager
        => s_manager ?? throw new InvalidOperationException("Call AppBuilder.UseNotifications(...) before using AvaloniaNotifications.Manager.");

    /// <summary>
    /// Starts creating the notification manager once Avalonia's platform is set up, without blocking
    /// startup on it.
    /// </summary>
    public static AppBuilder UseNotifications(this AppBuilder builder, AppIdentity identity)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentNullException.ThrowIfNull(identity);
        return builder.AfterSetup(_ => s_manager ??= CreateAsync(identity));
    }

    /// <summary>Creates a manager whose events are delivered on Avalonia's UI thread.</summary>
    public static async Task<INotificationManager> CreateAsync(AppIdentity identity, CancellationToken cancellationToken = default)
        => new DispatchingNotificationManager(await NotificationManager.CreateAsync(identity, cancellationToken).ConfigureAwait(false));
}
