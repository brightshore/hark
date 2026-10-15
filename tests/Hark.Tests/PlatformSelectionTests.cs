using AwesomeAssertions;

namespace Hark.Tests;

/// <summary>
/// The real platform entry point, in the test host. The host is never an app bundle and has no
/// notification daemon of its own, so what is asserted is that each platform declines cleanly,
/// with a reason, instead of crashing: on macOS an unguarded <c>currentNotificationCenter</c> raises
/// an Objective-C exception that kills the process outright.
/// </summary>
[TestClass]
public sealed class PlatformSelectionTests
{
    [TestMethod]
    public async Task On_macOS_an_unbundled_process_is_unsupported_with_the_reason()
    {
        if (!OperatingSystem.IsMacOS())
            Assert.Inconclusive("macOS only.");

        await using var manager = await NotificationManager.CreateAsync(new AppIdentity("com.example.Hark", "Hark Test"));

        manager.IsSupported.Should().BeFalse();
        manager.UnsupportedReason.Should().Contain(".app bundle");
    }

    /// <summary>
    /// Drives every WinRT call the backend makes (activation factories, the notifier and its
    /// setting, History, XML load, toast creation, Tag/Group, three event subscriptions, Show,
    /// Hide) through the real vtables. A wrong IID or slot fails here as an HRESULT, or as a crash.
    /// A runner session that cannot do toasts at all reports inconclusive with Windows' own reason,
    /// which is in the log either way.
    /// </summary>
    [TestMethod]
    public async Task On_Windows_a_toast_can_be_shown_and_closed_through_the_real_api()
    {
        if (!OperatingSystem.IsWindows())
            Assert.Inconclusive("Windows only.");

        await using var manager = await NotificationManager.CreateAsync(new AppIdentity("dev.brightshore.Hark.Tests", "Hark Tests"));
        Console.WriteLine($"Windows backend: supported={manager.IsSupported} reason={manager.UnsupportedReason ?? "(none)"} capabilities={manager.Capabilities}");
        if (!manager.IsSupported)
            Assert.Inconclusive($"This session cannot show toasts: {manager.UnsupportedReason}");

        var handle = await manager.ShowAsync(new Notification
        {
            Title = "Hark test toast",
            Body = "Shown and closed by the test suite.",
            Tag = "suite",
            Actions = [new("ok", "OK")],
            Silent = true,
        });
        var closes = new List<NotificationClosedEventArgs>();
        manager.Closed += (_, e) => closes.Add(e);
        await manager.CloseAsync(handle);

        handle.Id.Should().Be("t:suite");
        closes.Should().ContainSingle().Which.Reason.Should().Be(NotificationCloseReason.ClosedByApp);
    }
}
