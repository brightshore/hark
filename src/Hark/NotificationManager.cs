using Hark.Linux;
using Hark.MacOS;
using Hark.Windows;

namespace Hark;

/// <summary>Creates the notification manager for the current platform.</summary>
public static class NotificationManager
{
    /// <summary>
    /// Connects to the platform's notification service and discovers what it supports.
    /// Never throws for an unavailable service: it returns a manager whose
    /// <see cref="INotificationManager.IsSupported"/> is false, with the reason, so an app can
    /// start on a machine with no notification daemon and decide for itself what that means.
    /// </summary>
    public static async Task<INotificationManager> CreateAsync(AppIdentity identity, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(identity);
        ArgumentException.ThrowIfNullOrWhiteSpace(identity.AppId);
        ArgumentException.ThrowIfNullOrWhiteSpace(identity.DisplayName);

        return await CreateGuardedAsync(() => CreateForPlatformAsync(identity, cancellationToken), cancellationToken).ConfigureAwait(false);
    }

    private static async Task<INotificationManager> CreateForPlatformAsync(AppIdentity identity, CancellationToken cancellationToken)
    {
        if (OperatingSystem.IsLinux() || OperatingSystem.IsFreeBSD())
            return await FreeDesktopNotificationManager.CreateAsync(identity, cancellationToken).ConfigureAwait(false);

        if (OperatingSystem.IsMacOS())
            return await MacNotificationManager.CreateAsync(cancellationToken).ConfigureAwait(false);

        if (OperatingSystem.IsWindows())
            return WindowsNotificationManager.Create(identity);

        // Anything else has no notification service Hark knows. Saying so beats a silent no-op.
        return new UnsupportedNotificationManager($"Hark has no notification backend for {System.Runtime.InteropServices.RuntimeInformation.OSDescription} yet.");
    }

    /// <summary>
    /// The "never throws" promise, kept in one place: each backend turns the failures it knows into an
    /// unsupported manager, and anything else it didn't foresee (a socket refused mid-connect, a COM or
    /// Objective-C call failing in a way nobody has seen yet) becomes one here, with the error as the
    /// reason. An app that awaits its manager once at startup (Hark.Avalonia caches it) must not hold a
    /// faulted task for the rest of the run. Cancellation is still cancellation.
    /// </summary>
    internal static async Task<INotificationManager> CreateGuardedAsync(Func<Task<INotificationManager>> create, CancellationToken cancellationToken)
    {
        try
        {
            return await create().ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            return new UnsupportedNotificationManager($"The notification service could not be started: {ex.GetType().Name}: {ex.Message}");
        }
    }
}
