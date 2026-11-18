using Avalonia.Controls;
using Avalonia.Threading;
using Hark.Avalonia.Attention;

namespace Hark.Avalonia;

/// <summary>
/// Asks the user to look at a window that is not in front: the Dock icon bounces on macOS, the
/// taskbar button flashes on Windows, and the window is marked as demanding attention on X11.
/// </summary>
/// <remarks>
/// <para>
/// Make one per window and use it on the UI thread. Where the platform has no way to ask,
/// <see cref="IsSupported"/> is false and <see cref="UnsupportedReason"/> says why.
/// </para>
/// <para>
/// Avalonia 12 has no Wayland backend of its own: on a Wayland desktop its windows run under
/// XWayland, so they get the X11 request, and the compositor decides what to show for it.
/// </para>
/// </remarks>
public sealed class WindowAttention
{
    private readonly IAttentionPlatform? _platform;
    private readonly Action _verifyAccess;

    /// <summary>Finds how this platform asks for attention for <paramref name="topLevel"/>.</summary>
    /// <exception cref="InvalidOperationException">Not called on the UI thread.</exception>
    public WindowAttention(TopLevel topLevel)
    {
        ArgumentNullException.ThrowIfNull(topLevel);
        Dispatcher.UIThread.VerifyAccess();
        var handle = topLevel.TryGetPlatformHandle();
        (_platform, UnsupportedReason) = AttentionPlatforms.Create(handle?.HandleDescriptor, handle?.Handle ?? 0);
        _verifyAccess = Dispatcher.UIThread.VerifyAccess;
    }

    /// <summary>The seam the tests use.</summary>
    internal WindowAttention(IAttentionPlatform? platform, string? unsupportedReason, Action verifyAccess)
    {
        _platform = platform;
        UnsupportedReason = platform is null ? unsupportedReason : null;
        _verifyAccess = verifyAccess;
    }

    /// <summary>False when this platform, or this window, has no way to ask for attention. See <see cref="UnsupportedReason"/>.</summary>
    public bool IsSupported => _platform is not null;

    /// <summary>Why <see cref="IsSupported"/> is false; <c>null</c> when it is true.</summary>
    public string? UnsupportedReason { get; }

    /// <summary>What the request can do here.</summary>
    public AttentionCapabilities Capabilities => _platform?.Capabilities ?? AttentionCapabilities.None;

    /// <summary>
    /// Asks for attention. Platforms do nothing for a window, or an app, that is already in front.
    /// Where there is no <see cref="AttentionCapabilities.Critical"/>, a critical request is made as an
    /// informational one.
    /// </summary>
    /// <exception cref="InvalidOperationException">Not supported (the message carries the reason), or not called on the UI thread.</exception>
    public void Request(AttentionLevel level = AttentionLevel.Informational)
    {
        if (!Enum.IsDefined(level))
            throw new ArgumentOutOfRangeException(nameof(level), level, null);
        _verifyAccess();
        if (_platform is null)
            throw new InvalidOperationException($"Attention requests are not supported: {UnsupportedReason}");
        _platform.Request(level);
    }

    /// <summary>
    /// Withdraws this object's request, if it is still running. A no-op where there is nothing to cancel,
    /// or no <see cref="AttentionCapabilities.Cancel"/>; every platform also ends it when the window
    /// comes to the front.
    /// </summary>
    /// <exception cref="InvalidOperationException">Not called on the UI thread.</exception>
    public void Cancel()
    {
        _verifyAccess();
        _platform?.Cancel();
    }
}

/// <summary>How hard to ask.</summary>
public enum AttentionLevel
{
    /// <summary>Once: the Dock icon bounces once on macOS, the taskbar button flashes once on Windows.</summary>
    Informational,
    /// <summary>Until the user comes: the Dock icon bounces, or the taskbar button flashes, until the app is in front.</summary>
    Critical,
}

/// <summary>What an attention request can do on this platform.</summary>
[Flags]
public enum AttentionCapabilities
{
    /// <summary>Nothing: attention cannot be requested.</summary>
    None = 0,
    /// <summary>Attention can be requested.</summary>
    Request = 1 << 0,
    /// <summary><see cref="AttentionLevel.Critical"/> differs from <see cref="AttentionLevel.Informational"/>.</summary>
    Critical = 1 << 1,
    /// <summary><see cref="WindowAttention.Cancel"/> withdraws a request.</summary>
    Cancel = 1 << 2,
}
