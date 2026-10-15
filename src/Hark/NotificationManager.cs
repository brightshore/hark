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

        if (OperatingSystem.IsLinux() || OperatingSystem.IsFreeBSD())
            return await FreeDesktopNotificationManager.CreateAsync(identity, cancellationToken).ConfigureAwait(false);

        if (OperatingSystem.IsMacOS())
            return await MacNotificationManager.CreateAsync(cancellationToken).ConfigureAwait(false);

        if (OperatingSystem.IsWindows())
            return WindowsNotificationManager.Create(identity);

        // Anything else has no notification service Hark knows. Saying so beats a silent no-op.
        return new UnsupportedNotificationManager($"Hark has no notification backend for {System.Runtime.InteropServices.RuntimeInformation.OSDescription} yet.");
    }
}
