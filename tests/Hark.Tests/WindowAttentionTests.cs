using System.Runtime.InteropServices;
using AwesomeAssertions;
using Hark.Avalonia;
using Hark.Avalonia.Attention;
using static Hark.Avalonia.Attention.AttentionPlatforms;

namespace Hark.Tests;

[TestClass]
public sealed class WindowAttentionTests
{
    // ---- Which backend a window gets ----

    [TestMethod]
    [DataRow("MacOS", "NSWindow", "MacOS")]
    [DataRow("MacOS", "NSView", "MacOS")]
    [DataRow("Windows", "HWND", "Windows")]
    [DataRow("Unix", "XID", "X11")]
    public void Each_desktop_platform_gets_its_backend(string os, string descriptor, string backend)
    {
        var (chosen, reason) = Choose(Enum.Parse<HostOS>(os), descriptor, 0x42);

        chosen.Should().Be(Enum.Parse<Backend>(backend));
        reason.Should().BeNull();
    }

    [TestMethod]
    public void A_non_X11_window_on_Linux_is_unsupported_and_names_Wayland()
    {
        var (chosen, reason) = Choose(HostOS.Unix, "WaylandSurface", 0x42);

        chosen.Should().Be(Backend.None);
        reason.Should().Contain("'WaylandSurface'").And.Contain("xdg-activation");
    }

    [TestMethod]
    public void A_window_without_a_native_handle_is_unsupported()
    {
        Choose(HostOS.Windows, "HWND", 0).Backend.Should().Be(Backend.None);
        var (chosen, reason) = Choose(HostOS.Windows, null, 0x42);
        chosen.Should().Be(Backend.None);
        reason.Should().Contain("no native handle");
    }

    [TestMethod]
    public void An_unexpected_handle_on_Windows_is_unsupported()
        => Choose(HostOS.Windows, "XID", 0x42).Backend.Should().Be(Backend.None);

    [TestMethod]
    public void Another_os_is_unsupported()
        => Choose(HostOS.Other, "HWND", 0x42).Backend.Should().Be(Backend.None);

    // ---- WindowAttention over a platform ----

    [TestMethod]
    public void A_request_reaches_the_platform_at_its_level()
    {
        var platform = new FakeAttentionPlatform();
        var attention = new WindowAttention(platform, null, () => { });

        attention.Request();
        attention.Request(AttentionLevel.Critical);
        attention.Cancel();

        platform.Requests.Should().Equal(AttentionLevel.Informational, AttentionLevel.Critical);
        platform.Cancels.Should().Be(1);
        attention.IsSupported.Should().BeTrue();
        attention.UnsupportedReason.Should().BeNull();
        attention.Capabilities.Should().Be(platform.Capabilities);
    }

    [TestMethod]
    public void An_unsupported_request_throws_with_the_reason_and_cancel_is_a_no_op()
    {
        var attention = new WindowAttention(null, "no taskbar here", () => { });

        var act = () => attention.Request();

        act.Should().Throw<InvalidOperationException>().WithMessage("*no taskbar here*");
        attention.Cancel();
        attention.IsSupported.Should().BeFalse();
        attention.Capabilities.Should().Be(AttentionCapabilities.None);
    }

    [TestMethod]
    public void Off_the_UI_thread_nothing_reaches_the_platform()
    {
        var platform = new FakeAttentionPlatform();
        var attention = new WindowAttention(platform, null, () => throw new InvalidOperationException("Call from invalid thread"));

        var request = () => attention.Request();
        var cancel = () => attention.Cancel();

        request.Should().Throw<InvalidOperationException>();
        cancel.Should().Throw<InvalidOperationException>();
        platform.Requests.Should().BeEmpty();
        platform.Cancels.Should().Be(0);
    }

    [TestMethod]
    public void An_undefined_level_is_refused()
    {
        var platform = new FakeAttentionPlatform();
        var attention = new WindowAttention(platform, null, () => { });

        var act = () => attention.Request((AttentionLevel)7);

        act.Should().Throw<ArgumentOutOfRangeException>();
        platform.Requests.Should().BeEmpty();
    }

    // ---- What each backend sends ----

    [TestMethod]
    public void On_macOS_critical_bounces_until_the_app_is_active_and_informational_once()
    {
        MacAttention.RequestType(AttentionLevel.Critical).Should().Be(0u, "NSCriticalRequest");
        MacAttention.RequestType(AttentionLevel.Informational).Should().Be(10u, "NSInformationalRequest");
    }

    [TestMethod]
    public void On_Windows_critical_flashes_until_the_window_is_in_front_and_informational_once()
    {
        var critical = Win32Attention.Flash(0x42, AttentionLevel.Critical);
        var informational = Win32Attention.Flash(0x42, AttentionLevel.Informational);
        var stop = Win32Attention.Stop(0x42);

        (critical.Window, critical.Flags, critical.Count).Should().Be(((nint)0x42, 0x2u | 0xCu, 0u));
        (informational.Flags, informational.Count).Should().Be((0x2u, 1u));
        (stop.Flags, stop.Count).Should().Be((0u, 0u));
        critical.Size.Should().Be((uint)Marshal.SizeOf<Win32Attention.FlashInfo>());
    }

    [TestMethod]
    public void On_X11_the_state_message_adds_and_removes_demands_attention()
    {
        var add = X11Attention.StateMessage(window: 0x400001, stateAtom: 300, attentionAtom: 301, demands: true);
        var remove = X11Attention.StateMessage(window: 0x400001, stateAtom: 300, attentionAtom: 301, demands: false);

        (add.Type, add.Window, add.MessageType, add.Format).Should().Be((33, (nuint)0x400001, (nuint)300, 32));
        (add.Data0, add.Data1, add.Data2, add.Data3).Should().Be(((nint)1, (nint)301, (nint)0, (nint)1));
        remove.Data0.Should().Be(0);
    }

    /// <summary>
    /// XSendEvent reads a whole XEvent, and the X server reads the fields at Xlib's offsets: on any
    /// word size, the window is the fifth word, the format the seventh, the data from the eighth.
    /// </summary>
    [TestMethod]
    public void The_X11_client_message_has_Xlibs_layout()
    {
        var word = IntPtr.Size;
        Marshal.SizeOf<X11Attention.XClientMessageEvent>().Should().Be(24 * word);
        Marshal.OffsetOf<X11Attention.XClientMessageEvent>(nameof(X11Attention.XClientMessageEvent.Window)).Should().Be(4 * word);
        Marshal.OffsetOf<X11Attention.XClientMessageEvent>(nameof(X11Attention.XClientMessageEvent.MessageType)).Should().Be(5 * word);
        Marshal.OffsetOf<X11Attention.XClientMessageEvent>(nameof(X11Attention.XClientMessageEvent.Format)).Should().Be(6 * word);
        Marshal.OffsetOf<X11Attention.XClientMessageEvent>(nameof(X11Attention.XClientMessageEvent.Data0)).Should().Be(7 * word);
    }
}
