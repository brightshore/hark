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

    [TestMethod]
    public async Task On_Windows_the_missing_backend_says_so()
    {
        if (!OperatingSystem.IsWindows())
            Assert.Inconclusive("Windows only.");

        await using var manager = await NotificationManager.CreateAsync(new AppIdentity("com.example.Hark", "Hark Test"));

        manager.IsSupported.Should().BeFalse();
        manager.UnsupportedReason.Should().Contain("no notification backend");
    }
}
