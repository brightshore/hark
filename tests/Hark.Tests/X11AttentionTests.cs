using System.Runtime.InteropServices;
using AwesomeAssertions;
using Hark.Avalonia;
using Hark.Avalonia.Attention;

namespace Hark.Tests;

/// <summary>
/// The X11 request over a fake Xlib: the error guard is in place for the whole life of the connection,
/// errors on it become an exception instead of Xlib's default exit(), and other connections' errors
/// still reach the handler that was there before.
/// </summary>
[TestClass]
public sealed unsafe class X11AttentionTests
{
    private const nuint Window = 0x400001;

    [TestMethod]
    public void The_guard_is_installed_before_the_request_and_restored_after_the_connection_closes()
    {
        var xlib = new FakeXlib();

        new X11Attention(Window, xlib).Request(AttentionLevel.Informational);

        xlib.Calls.Should().Equal(
            "open",
            "handler guard",
            "atom _NET_WM_STATE",
            "atom _NET_WM_STATE_DEMANDS_ATTENTION",
            "send add 301",
            "sync",
            "close",
            $"handler {FakeXlib.PreviousHandler}");
    }

    [TestMethod]
    public void Cancel_removes_the_state_under_the_same_guard()
    {
        var xlib = new FakeXlib();

        new X11Attention(Window, xlib).Cancel();

        xlib.Calls.Should().ContainInOrder("handler guard", "send remove 301", "sync", "close", $"handler {FakeXlib.PreviousHandler}");
    }

    [TestMethod]
    public void An_error_on_the_connection_becomes_an_exception_after_the_handler_is_restored()
    {
        var xlib = new FakeXlib { DuringSync = display => Raise(display, errorCode: 3) };

        var act = () => new X11Attention(Window, xlib).Request(AttentionLevel.Informational);

        act.Should().Throw<InvalidOperationException>().WithMessage("*error code 3*");
        xlib.Calls[^1].Should().Be($"handler {FakeXlib.PreviousHandler}");
    }

    [TestMethod]
    public void A_failed_send_still_closes_the_connection_and_restores_the_handler()
    {
        var xlib = new FakeXlib { SendThrows = true };

        var act = () => new X11Attention(Window, xlib).Request(AttentionLevel.Informational);

        act.Should().Throw<InvalidOperationException>().WithMessage("send failed");
        xlib.Calls.Should().EndWith(["close", $"handler {FakeXlib.PreviousHandler}"]);
    }

    [TestMethod]
    public void Another_connections_errors_go_to_the_previous_handler()
    {
        s_forwardedFrom = IntPtr.Zero;
        X11ErrorGuard.Begin(FakeXlib.Display, (IntPtr)(delegate* unmanaged<IntPtr, X11ErrorGuard.XErrorEvent*, int>)&Previous);
        try
        {
            Raise(0x9999, errorCode: 8);
            Raise(FakeXlib.Display, errorCode: 3);
        }
        finally
        {
            X11ErrorGuard.End().Should().Be(3, "only this connection's error is the guard's");
        }

        s_forwardedFrom.Should().Be(0x9999);
    }

    [TestMethod]
    public void The_X_error_event_has_Xlibs_layout()
    {
        var word = IntPtr.Size;
        Marshal.OffsetOf<X11ErrorGuard.XErrorEvent>(nameof(X11ErrorGuard.XErrorEvent.Display)).Should().Be(word);
        Marshal.OffsetOf<X11ErrorGuard.XErrorEvent>(nameof(X11ErrorGuard.XErrorEvent.ErrorCode)).Should().Be(4 * word);
    }

    private static IntPtr s_forwardedFrom;

    [UnmanagedCallersOnly]
    private static int Previous(IntPtr display, X11ErrorGuard.XErrorEvent* error)
    {
        s_forwardedFrom = display;
        return 0;
    }

    private static void Raise(IntPtr display, byte errorCode)
    {
        var error = new X11ErrorGuard.XErrorEvent { Type = 0, Display = display, ErrorCode = errorCode };
        X11ErrorGuard.Handle(display, &error);
    }
}
