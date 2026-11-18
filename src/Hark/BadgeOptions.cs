namespace Hark;

/// <summary>How <see cref="BadgeManager.CreateAsync(AppIdentity, BadgeOptions, CancellationToken)"/> finds where the badge goes.</summary>
public sealed record BadgeOptions
{
    /// <summary>
    /// Windows only: the window (HWND) whose taskbar button carries the badge, asked every time the
    /// badge changes, since windows come and go. Return 0 when there is none. When <c>null</c>, the
    /// process's main window is used: its first visible top-level window. Hark.Avalonia passes the
    /// app's main window.
    /// </summary>
    public Func<nint>? WindowHandle { get; init; }
}
