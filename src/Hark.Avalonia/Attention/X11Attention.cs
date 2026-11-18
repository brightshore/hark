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
/// <para>
/// There is one level of attention on X11: the window manager decides how to show it, and clears it
/// when the window is activated.
/// </para>
/// <para>
/// X errors arrive later than the call that caused them, and Xlib's default handler ends the process.
/// So while the connection is open, <see cref="X11ErrorGuard"/> is installed: it notes an error on this
/// connection (made into an exception once the connection is closed) and passes any other connection's
/// errors to the handler that was there before. <c>XSync</c> collects the errors while it is installed.
/// </para>
/// </remarks>
internal sealed class X11Attention(nuint window, IXlib xlib) : IAttentionPlatform
{
    internal const int ClientMessage = 33;
    internal const nint NetWmStateRemove = 0;
    internal const nint NetWmStateAdd = 1;

    /// <summary>EWMH source indication: a normal application.</summary>
    internal const nint SourceApplication = 1;

    public AttentionCapabilities Capabilities => AttentionCapabilities.Request | AttentionCapabilities.Cancel;

    public void Request(AttentionLevel level) => SetDemandsAttention(true);

    public void Cancel() => SetDemandsAttention(false);

    private void SetDemandsAttention(bool demands)
    {
        var display = xlib.OpenDisplay();
        if (display == IntPtr.Zero)
            throw new InvalidOperationException("The X display could not be opened, so attention could not be requested.");

        byte error;
        var previous = xlib.SetErrorHandler(X11ErrorGuard.Handler);
        X11ErrorGuard.Begin(display, previous);
        try
        {
            var message = StateMessage(window, xlib.InternAtom(display, "_NET_WM_STATE"),
                xlib.InternAtom(display, "_NET_WM_STATE_DEMANDS_ATTENTION"), demands);
            xlib.SendEvent(display, xlib.DefaultRootWindow(display), message);
            xlib.Sync(display);
        }
        finally
        {
            xlib.CloseDisplay(display);
            error = X11ErrorGuard.End();
            xlib.SetErrorHandler(previous);
        }

        if (error != 0)
            throw new InvalidOperationException($"The X server refused the attention request with error code {error}.");
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

/// <summary>The Xlib calls <see cref="X11Attention"/> makes, so its error handling is testable without a display.</summary>
internal interface IXlib
{
    IntPtr OpenDisplay();

    void CloseDisplay(IntPtr display);

    nuint DefaultRootWindow(IntPtr display);

    nuint InternAtom(IntPtr display, string name);

    void SendEvent(IntPtr display, nuint destination, X11Attention.XClientMessageEvent message);

    /// <summary>Flushes and waits for the server, so any error the requests caused has been handled on return.</summary>
    void Sync(IntPtr display);

    /// <summary><c>XSetErrorHandler</c>: installs <paramref name="handler"/> and returns the one it replaced.</summary>
    IntPtr SetErrorHandler(IntPtr handler);
}

/// <summary>
/// The error handler installed while <see cref="X11Attention"/> has its connection open. Xlib's handler
/// is process-wide, and requests come only from the UI thread, so one connection is watched at a time.
/// </summary>
internal static unsafe class X11ErrorGuard
{
    private static IntPtr s_display;
    private static IntPtr s_previous;
    private static byte s_error;

    /// <summary>The <c>XErrorHandler</c> to install: <c>int (*)(Display *, XErrorEvent *)</c>.</summary>
    public static IntPtr Handler => (IntPtr)(delegate* unmanaged<IntPtr, XErrorEvent*, int>)&OnError;

    /// <summary>Starts watching <paramref name="display"/>; <paramref name="previous"/> gets every other connection's errors.</summary>
    public static void Begin(IntPtr display, IntPtr previous)
    {
        s_display = display;
        s_previous = previous;
        s_error = 0;
    }

    /// <summary>Stops watching, and returns the first error code seen on the connection, or 0.</summary>
    public static byte End()
    {
        var error = s_error;
        s_display = IntPtr.Zero;
        s_previous = IntPtr.Zero;
        s_error = 0;
        return error;
    }

    [UnmanagedCallersOnly]
    private static int OnError(IntPtr display, XErrorEvent* error) => Handle(display, error);

    internal static int Handle(IntPtr display, XErrorEvent* error)
    {
        if (display == s_display && s_display != IntPtr.Zero)
        {
            if (s_error == 0)
                s_error = error->ErrorCode;
            return 0;
        }

        // Another connection's error, Avalonia's most likely: what was installed before decides.
        return s_previous != IntPtr.Zero ? ((delegate* unmanaged<IntPtr, XErrorEvent*, int>)s_previous)(display, error) : 0;
    }

    /// <summary><c>XErrorEvent</c>.</summary>
    [StructLayout(LayoutKind.Sequential)]
    internal struct XErrorEvent
    {
        public int Type;
        public IntPtr Display;
        public nuint ResourceId;
        public nuint Serial;
        public byte ErrorCode;
        public byte RequestCode;
        public byte MinorCode;
    }
}

/// <summary><see cref="IXlib"/> over <c>libX11</c>.</summary>
[SupportedOSPlatform("linux")]
[SupportedOSPlatform("freebsd")]
internal sealed unsafe class Xlib : IXlib
{
    private const string LibX11 = "libX11.so.6";

    private const nint SubstructureNotifyMask = 1 << 19;
    private const nint SubstructureRedirectMask = 1 << 20;

    [DllImport(LibX11, EntryPoint = "XOpenDisplay")]
    private static extern IntPtr XOpenDisplay(IntPtr name);

    [DllImport(LibX11, EntryPoint = "XCloseDisplay")]
    private static extern int XCloseDisplay(IntPtr display);

    [DllImport(LibX11, EntryPoint = "XDefaultRootWindow")]
    private static extern nuint XDefaultRootWindow(IntPtr display);

    [DllImport(LibX11, EntryPoint = "XInternAtom")]
    private static extern nuint XInternAtom(IntPtr display, byte* name, int onlyIfExists);

    [DllImport(LibX11, EntryPoint = "XSendEvent")]
    private static extern int XSendEvent(IntPtr display, nuint window, int propagate, nint eventMask, X11Attention.XClientMessageEvent* message);

    [DllImport(LibX11, EntryPoint = "XSync")]
    private static extern int XSync(IntPtr display, int discard);

    [DllImport(LibX11, EntryPoint = "XSetErrorHandler")]
    private static extern IntPtr XSetErrorHandler(IntPtr handler);

    public IntPtr OpenDisplay() => XOpenDisplay(IntPtr.Zero);

    public void CloseDisplay(IntPtr display) => XCloseDisplay(display);

    public nuint DefaultRootWindow(IntPtr display) => XDefaultRootWindow(display);

    public nuint InternAtom(IntPtr display, string name)
    {
        var bytes = System.Text.Encoding.ASCII.GetBytes(name + "\0");
        fixed (byte* p = bytes)
            return XInternAtom(display, p, 0);
    }

    public void SendEvent(IntPtr display, nuint destination, X11Attention.XClientMessageEvent message)
        => XSendEvent(display, destination, 0, SubstructureRedirectMask | SubstructureNotifyMask, &message);

    public void Sync(IntPtr display) => XSync(display, 0);

    public IntPtr SetErrorHandler(IntPtr handler) => XSetErrorHandler(handler);
}
