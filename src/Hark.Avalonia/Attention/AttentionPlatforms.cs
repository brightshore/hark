namespace Hark.Avalonia.Attention;

/// <summary>One platform's way of asking for attention. <see cref="WindowAttention"/> talks only to this.</summary>
internal interface IAttentionPlatform
{
    AttentionCapabilities Capabilities { get; }

    void Request(AttentionLevel level);

    void Cancel();
}

/// <summary>Which backend a window gets, from the OS and the kind of native handle Avalonia gives it.</summary>
internal static class AttentionPlatforms
{
    internal enum Backend
    {
        None,
        MacOS,
        Windows,
        X11,
    }

    internal enum HostOS
    {
        Other,
        MacOS,
        Windows,
        Unix,
    }

    public static (IAttentionPlatform? Platform, string? Reason) Create(string? descriptor, nint handle)
    {
        var (backend, reason) = Choose(CurrentOS(), descriptor, handle);
        IAttentionPlatform? platform = null;
        if (backend == Backend.MacOS && OperatingSystem.IsMacOS())
            platform = new MacAttention();
        else if (backend == Backend.Windows && OperatingSystem.IsWindows())
            platform = new Win32Attention(handle);
        else if (backend == Backend.X11 && (OperatingSystem.IsLinux() || OperatingSystem.IsFreeBSD()))
            platform = new X11Attention((nuint)handle, new Xlib());
        return (platform, reason);
    }

    internal static HostOS CurrentOS() =>
        OperatingSystem.IsMacOS() ? HostOS.MacOS
        : OperatingSystem.IsWindows() ? HostOS.Windows
        : OperatingSystem.IsLinux() || OperatingSystem.IsFreeBSD() ? HostOS.Unix
        : HostOS.Other;

    /// <summary>
    /// The decision, apart from anything native. Avalonia names its handles "NSWindow" (or "NSView")
    /// on macOS, "HWND" on Windows and "XID" on X11.
    /// </summary>
    internal static (Backend Backend, string? Reason) Choose(HostOS os, string? descriptor, nint handle)
    {
        if (descriptor is null || handle == 0)
            return (Backend.None, "This window has no native handle: it is headless, or has not been created on a desktop platform.");

        return os switch
        {
            // The Dock icon is the app's, so any window of it will do.
            HostOS.MacOS => (Backend.MacOS, null),
            HostOS.Windows when descriptor == "HWND" => (Backend.Windows, null),
            HostOS.Unix when descriptor == "XID" => (Backend.X11, null),
            HostOS.Unix => (Backend.None,
                $"Hark can ask for attention for X11 windows, and this one is a '{descriptor}'. On Wayland that takes the "
                + "xdg-activation protocol and the window's wl_surface, which Avalonia does not expose."),
            _ => (Backend.None, $"Hark has no attention request for a '{descriptor}' window on this platform."),
        };
    }
}
