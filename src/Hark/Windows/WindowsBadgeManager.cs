using System.Diagnostics;
using System.Runtime.Versioning;

namespace Hark.Windows;

/// <summary>
/// The badge as a taskbar overlay icon (<c>ITaskbarList3::SetOverlayIcon</c>) and the taskbar
/// button's progress bar (<c>SetProgressValue</c>).
/// </summary>
/// <remarks>
/// The overlay belongs to a window's taskbar button, so each change asks for the window again: the
/// <see cref="BadgeOptions.WindowHandle"/> given, or the process's main window. The count is drawn by
/// <see cref="BadgeRenderer"/>, at the window's scaling. An overlay has room for a count and no more,
/// so there is no <see cref="BadgeCapabilities.Text"/> here.
/// </remarks>
internal sealed class WindowsBadgeManager : IBadgeManager
{
    private const BadgeCapabilities WindowsCapabilities = BadgeCapabilities.Count | BadgeCapabilities.Progress;

    private readonly ITaskbar _taskbar;
    private readonly Func<nint> _window;
    private bool _disposed;

    private WindowsBadgeManager(ITaskbar taskbar, Func<nint> window)
    {
        _taskbar = taskbar;
        _window = window;
    }

    public bool IsSupported => true;
    public string? UnsupportedReason => null;
    public BadgeCapabilities Capabilities => WindowsCapabilities;

    [SupportedOSPlatform("windows")]
    public static IBadgeManager Create(Func<nint>? window) => Create(new Taskbar(), window ?? MainWindow);

    /// <summary>The seam the tests use.</summary>
    internal static WindowsBadgeManager Create(ITaskbar taskbar, Func<nint> window) => new(taskbar, window);

    /// <summary>What <see cref="Process.MainWindowHandle"/> finds: the first visible top-level window without an owner.</summary>
    private static nint MainWindow()
    {
        using var process = Process.GetCurrentProcess();
        return process.MainWindowHandle;
    }

    public Task SetCountAsync(int count, CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        BadgeValidation.ValidateCount(count);
        cancellationToken.ThrowIfCancellationRequested();

        var window = RequireWindow();
        if (count == 0)
            _taskbar.SetOverlay(window, null, null);
        else
            _taskbar.SetOverlay(window, BadgeRenderer.RenderCount(count, _taskbar.OverlaySize(window)), BadgeRenderer.CountText(count));
        return Task.CompletedTask;
    }

    public Task SetTextAsync(string? text, CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        BadgeValidation.RequireCapability(WindowsCapabilities, BadgeCapabilities.Text);
        return Task.CompletedTask;
    }

    public Task SetProgressAsync(double? progress, CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        BadgeValidation.ValidateProgress(progress);
        cancellationToken.ThrowIfCancellationRequested();

        _taskbar.SetProgress(RequireWindow(), progress);
        return Task.CompletedTask;
    }

    public Task ClearAsync(CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        // No window means no taskbar button, and so nothing to clear.
        var window = _window();
        if (window != 0)
        {
            _taskbar.SetOverlay(window, null, null);
            _taskbar.SetProgress(window, null);
        }
        return Task.CompletedTask;
    }

    private nint RequireWindow()
    {
        var window = _window();
        return window != 0
            ? window
            : throw new InvalidOperationException("Windows shows the badge on a window's taskbar button, and the app has no window yet.");
    }

    public ValueTask DisposeAsync()
    {
        _disposed = true;
        return ValueTask.CompletedTask;
    }
}
