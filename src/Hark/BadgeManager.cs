using Hark.Linux;
using Hark.MacOS;
using Hark.Windows;

namespace Hark;

/// <summary>Creates the app-icon badge for the current platform.</summary>
public static class BadgeManager
{
    /// <inheritdoc cref="CreateAsync(AppIdentity, BadgeOptions, CancellationToken)"/>
    public static Task<IBadgeManager> CreateAsync(AppIdentity identity, CancellationToken cancellationToken = default)
        => CreateAsync(identity, new BadgeOptions(), cancellationToken);

    /// <summary>
    /// Finds the platform's badge and what it can show. Never throws for a platform without one: it
    /// returns a manager whose <see cref="IBadgeManager.IsSupported"/> is false, with the reason.
    /// </summary>
    /// <remarks>
    /// On macOS, create it once the app's UI framework has started (Hark.Avalonia's
    /// <c>UseBadge</c> does): the Dock tile belongs to the process's <c>NSApplication</c>.
    /// </remarks>
    public static async Task<IBadgeManager> CreateAsync(AppIdentity identity, BadgeOptions options, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(identity);
        ArgumentException.ThrowIfNullOrWhiteSpace(identity.AppId);
        ArgumentNullException.ThrowIfNull(options);

        return await CreateGuardedAsync(() => CreateForPlatformAsync(identity, options, cancellationToken), cancellationToken).ConfigureAwait(false);
    }

    private static async Task<IBadgeManager> CreateForPlatformAsync(AppIdentity identity, BadgeOptions options, CancellationToken cancellationToken)
    {
        if (OperatingSystem.IsLinux() || OperatingSystem.IsFreeBSD())
            return await LauncherEntryBadgeManager.CreateAsync(identity, cancellationToken).ConfigureAwait(false);

        if (OperatingSystem.IsMacOS())
            return MacBadgeManager.Create();

        if (OperatingSystem.IsWindows())
            return WindowsBadgeManager.Create(options.WindowHandle);

        return new UnsupportedBadgeManager($"Hark has no app badge for {System.Runtime.InteropServices.RuntimeInformation.OSDescription} yet.");
    }

    /// <summary>The same "never throws" promise as <see cref="NotificationManager"/>'s, for the badge.</summary>
    internal static async Task<IBadgeManager> CreateGuardedAsync(Func<Task<IBadgeManager>> create, CancellationToken cancellationToken)
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
            return new UnsupportedBadgeManager($"The app badge could not be set up: {ex.GetType().Name}: {ex.Message}");
        }
    }
}
