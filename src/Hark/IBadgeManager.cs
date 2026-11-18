namespace Hark;

/// <summary>
/// Puts a count or a short label on the app's icon: the Dock tile on macOS, the taskbar button on
/// Windows, the launcher entry on Linux. Get one from <see cref="BadgeManager.CreateAsync(AppIdentity, CancellationToken)"/>.
/// </summary>
/// <remarks>
/// The badge belongs to the app, not to a notification: it stays until the app changes or clears it,
/// and goes when the app exits. Check <see cref="Capabilities"/> before calling a method, because
/// what the icon can show differs by platform.
/// </remarks>
public interface IBadgeManager : IAsyncDisposable
{
    /// <summary>False when this platform, or this process, has no badge. See <see cref="UnsupportedReason"/>.</summary>
    bool IsSupported { get; }

    /// <summary>Why <see cref="IsSupported"/> is false; <c>null</c> when it is true.</summary>
    string? UnsupportedReason { get; }

    /// <summary>What the badge can show here.</summary>
    BadgeCapabilities Capabilities { get; }

    /// <summary>Shows <paramref name="count"/> on the icon, replacing any count or label. Zero clears it.</summary>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="count"/> is negative.</exception>
    /// <exception cref="InvalidOperationException">The manager is not supported, or the badge has nowhere to go yet; the message says why.</exception>
    /// <exception cref="NotSupportedException">The platform has no <see cref="BadgeCapabilities.Count"/>.</exception>
    Task SetCountAsync(int count, CancellationToken cancellationToken = default);

    /// <summary>Shows a short label on the icon, replacing any count or label. <c>null</c> or empty clears it.</summary>
    /// <exception cref="InvalidOperationException">The manager is not supported; the message carries the reason.</exception>
    /// <exception cref="NotSupportedException">The platform has no <see cref="BadgeCapabilities.Text"/>.</exception>
    Task SetTextAsync(string? text, CancellationToken cancellationToken = default);

    /// <summary>Shows a progress bar on the icon, from 0 to 1. <c>null</c> hides it.</summary>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="progress"/> is outside 0 to 1.</exception>
    /// <exception cref="InvalidOperationException">The manager is not supported, or the badge has nowhere to go yet; the message says why.</exception>
    /// <exception cref="NotSupportedException">The platform has no <see cref="BadgeCapabilities.Progress"/>.</exception>
    Task SetProgressAsync(double? progress, CancellationToken cancellationToken = default);

    /// <summary>Removes the count or label and the progress bar. A no-op where there is no badge.</summary>
    Task ClearAsync(CancellationToken cancellationToken = default);
}
