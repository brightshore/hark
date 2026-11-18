namespace Hark.Windows;

/// <summary>
/// The taskbar surface Hark uses, and nothing else. <see cref="WindowsBadgeManager"/> talks only to
/// this, so it is testable on any OS. <see cref="Taskbar"/> is the one real implementation.
/// </summary>
internal interface ITaskbar
{
    /// <summary>The overlay icon's size in pixels for <paramref name="window"/>: 16 at 100% scaling.</summary>
    int OverlaySize(nint window);

    /// <summary>Sets the overlay on <paramref name="window"/>'s taskbar button; a null icon removes it.</summary>
    void SetOverlay(nint window, BadgeIcon? icon, string? description);

    /// <summary>Sets the progress bar on <paramref name="window"/>'s taskbar button, 0 to 1; null removes it.</summary>
    void SetProgress(nint window, double? progress);
}
