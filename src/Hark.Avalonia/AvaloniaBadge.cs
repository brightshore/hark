using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Threading;

namespace Hark.Avalonia;

/// <summary>Avalonia setup for Hark's app-icon badge.</summary>
public static class AvaloniaBadge
{
    private static Task<IBadgeManager>? s_manager;

    /// <summary>The app's badge, created by <see cref="UseBadge"/>.</summary>
    /// <exception cref="InvalidOperationException"><see cref="UseBadge"/> was not called.</exception>
    public static Task<IBadgeManager> Manager
        => s_manager ?? throw new InvalidOperationException("Call AppBuilder.UseBadge(...) before using AvaloniaBadge.Manager.");

    /// <summary>
    /// Creates the badge once Avalonia's platform is set up, which on macOS is when the Dock tile
    /// exists. On Windows the badge goes on the main window's taskbar button.
    /// </summary>
    public static AppBuilder UseBadge(this AppBuilder builder, AppIdentity identity)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentNullException.ThrowIfNull(identity);
        return builder.AfterSetup(_ => s_manager ??= CreateAsync(identity));
    }

    /// <summary>Creates a badge that, on Windows, goes on the main window of the app's desktop lifetime.</summary>
    public static Task<IBadgeManager> CreateAsync(AppIdentity identity, CancellationToken cancellationToken = default)
        => BadgeManager.CreateAsync(identity, new BadgeOptions { WindowHandle = MainWindowHandle }, cancellationToken);

    /// <summary>The main window's HWND, read on the UI thread; 0 before there is one.</summary>
    private static nint MainWindowHandle()
    {
        static nint Read() => (Application.Current?.ApplicationLifetime as IClassicDesktopStyleApplicationLifetime)
            ?.MainWindow?.TryGetPlatformHandle()?.Handle ?? 0;

        return Dispatcher.UIThread.CheckAccess() ? Read() : Dispatcher.UIThread.Invoke(Read);
    }
}
