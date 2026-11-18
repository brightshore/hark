using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;

namespace Hark.Avalonia.Attention;

/// <summary>
/// EWMH's <c>_NET_WM_STATE_DEMANDS_ATTENTION</c>, set and cleared by the <c>_NET_WM_STATE</c> client
/// message to the root window that the spec prescribes. Any connection to the display may send it,
/// so this opens its own rather than reaching into Avalonia's.
/// </summary>
/// <remarks>
/// There is one level of attention on X11: the window manager decides how to show it, and clears it
/// when the window is activated. Only the members that call Xlib are Unix-only, so the message is
/// testable anywhere.
/// </remarks>
internal sealed unsafe class X11Attention(nuint window) : IAttentionPlatform
{
    private const string LibX11 = "libX11.so.6";

    internal const int ClientMessage = 33;
    internal const nint NetWmStateRemove = 0;
    internal const nint NetWmStateAdd = 1;

    /// <summary>EWMH source indication: a normal application.</summary>
    internal const nint SourceApplication = 1;

    private const nint SubstructureNotifyMask = 1 << 19;
    private const nint SubstructureRedirectMask = 1 << 20;

    [DllImport(LibX11)]
    private static extern IntPtr XOpenDisplay(IntPtr name);

    [DllImport(LibX11)]
    private static extern int XCloseDisplay(IntPtr display);

    [DllImport(LibX11)]
    private static extern nuint XDefaultRootWindow(IntPtr display);

    [DllImport(LibX11)]
    private static extern nuint XInternAtom(IntPtr display, byte* name, int onlyIfExists);

    [DllImport(LibX11)]
    private static extern int XSendEvent(IntPtr display, nuint window, int propagate, nint eventMask, XClientMessageEvent* message);

    [DllImport(LibX11)]
    private static extern int XFlush(IntPtr display);

    public AttentionCapabilities Capabilities => AttentionCapabilities.Request | AttentionCapabilities.Cancel;

    [SupportedOSPlatform("linux")]
    [SupportedOSPlatform("freebsd")]
    public void Request(AttentionLevel level) => SetDemandsAttention(true);

    [SupportedOSPlatform("linux")]
    [SupportedOSPlatform("freebsd")]
    public void Cancel() => SetDemandsAttention(false);

    [SupportedOSPlatform("linux")]
    [SupportedOSPlatform("freebsd")]
    private void SetDemandsAttention(bool demands)
    {
        var display = XOpenDisplay(IntPtr.Zero);
        if (display == IntPtr.Zero)
            throw new InvalidOperationException("The X display could not be opened, so attention could not be requested.");
        try
        {
            nuint state, attention;
            fixed (byte* name = "_NET_WM_STATE\0"u8)
                state = XInternAtom(display, name, 0);
            fixed (byte* name = "_NET_WM_STATE_DEMANDS_ATTENTION\0"u8)
                attention = XInternAtom(display, name, 0);

            var message = StateMessage(window, state, attention, demands);
            XSendEvent(display, XDefaultRootWindow(display), 0, SubstructureRedirectMask | SubstructureNotifyMask, &message);
            XFlush(display);
        }
        finally
        {
            XCloseDisplay(display);
        }
    }

    /// <summary>The <c>_NET_WM_STATE</c> request adding or removing the attention state, as EWMH lays it out.</summary>
    internal static XClientMessageEvent StateMessage(nuint window, nuint stateAtom, nuint attentionAtom, bool demands) => new()
    {
        Type = ClientMessage,
        Window = window,
        MessageType = stateAtom,
        Format = 32,
        Data0 = demands ? NetWmStateAdd : NetWmStateRemove,
        Data1 = (nint)attentionAtom,
        Data2 = 0,
        Data3 = SourceApplication,
    };

    /// <summary>
    /// <c>XClientMessageEvent</c>, padded to the size of the <c>XEvent</c> union, all 24 longs of which
    /// <c>XSendEvent</c> reads. C's <c>long</c> and pointers are <see cref="nint"/> here.
    /// </summary>
    [StructLayout(LayoutKind.Sequential)]
    internal struct XClientMessageEvent
    {
        public int Type;
        public nuint Serial;
        public int SendEvent;
        public IntPtr Display;
        public nuint Window;
        public nuint MessageType;
        public int Format;
        public nint Data0;
        public nint Data1;
        public nint Data2;
        public nint Data3;
        public nint Data4;
        private Padding _padding;

        [InlineArray(12)]
        private struct Padding
        {
            private nint _element;
        }
    }
}
