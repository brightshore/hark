using System.Runtime.InteropServices;
using System.Runtime.Versioning;

namespace Hark.Avalonia.Attention;

/// <summary><c>FlashWindowEx</c> on the window's taskbar button.</summary>
/// <remarks>Only the members that call user32 are Windows-only, so the flags are testable anywhere.</remarks>
internal sealed class Win32Attention(nint window) : IAttentionPlatform
{
    internal const uint FLASHW_STOP = 0;
    internal const uint FLASHW_TRAY = 0x2;
    internal const uint FLASHW_TIMERNOFG = 0xC;

    [DllImport("user32.dll", ExactSpelling = true)]
    private static extern int FlashWindowEx(ref FlashInfo info);

    [DllImport("user32.dll", ExactSpelling = true)]
    private static extern IntPtr GetForegroundWindow();

    public AttentionCapabilities Capabilities => AttentionCapabilities.Request | AttentionCapabilities.Critical | AttentionCapabilities.Cancel;

    [SupportedOSPlatform("windows")]
    public void Request(AttentionLevel level)
    {
        // A timed flash stops by itself when the window comes forward; a single one has to be held back
        // from a window already in front, which macOS would not bounce for either.
        if (level == AttentionLevel.Informational && GetForegroundWindow() == window)
            return;
        var info = Flash(window, level);
        FlashWindowEx(ref info);
    }

    [SupportedOSPlatform("windows")]
    public void Cancel()
    {
        var info = Stop(window);
        FlashWindowEx(ref info);
    }

    /// <summary>
    /// Critical flashes until the window comes to the front. Informational flashes once; Windows then
    /// leaves the button highlighted until the user switches to it.
    /// </summary>
    internal static FlashInfo Flash(nint window, AttentionLevel level) => level == AttentionLevel.Critical
        ? FlashInfo.For(window, FLASHW_TRAY | FLASHW_TIMERNOFG, count: 0)
        : FlashInfo.For(window, FLASHW_TRAY, count: 1);

    internal static FlashInfo Stop(nint window) => FlashInfo.For(window, FLASHW_STOP, count: 0);

    /// <summary><c>FLASHWINFO</c>.</summary>
    [StructLayout(LayoutKind.Sequential)]
    internal struct FlashInfo
    {
        public uint Size;
        public IntPtr Window;
        public uint Flags;
        public uint Count;
        public uint Timeout;

        public static FlashInfo For(nint window, uint flags, uint count) => new()
        {
            Size = (uint)Marshal.SizeOf<FlashInfo>(),
            Window = window,
            Flags = flags,
            Count = count,
        };
    }
}
