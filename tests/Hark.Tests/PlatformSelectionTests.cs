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
    // The sample app's image, copied next to the test binaries, so the image path is part of the
    // real-API run rather than only of the XML the unit tests inspect.
    private static readonly string SampleImage = Path.Combine(AppContext.BaseDirectory, "Assets", "hark-sample.png");

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
    /// <summary>
    /// The COM activator is what makes a click after exit start the app. A click cannot be made
    /// in CI, so what is asserted is everything Windows needs to make that call: the class object
    /// registered with COM, the CLSID's launch command, and the AUMID's link to the CLSID.
    /// </summary>
    [System.Runtime.Versioning.SupportedOSPlatform("windows")]
    private static void AssertActivatorRegistered(Hark.Windows.WindowsNotificationManager manager, string appId)
    {
        manager.ActivatorUnavailableReason.Should().BeNull();
        var clsid = Hark.Windows.ToastArguments.ActivatorClsid(appId).ToString("B");
        using var aumid = Microsoft.Win32.Registry.CurrentUser.OpenSubKey($@"Software\Classes\AppUserModelId\{appId}");
        aumid!.GetValue("CustomActivator").Should().Be(clsid);
        using var server = Microsoft.Win32.Registry.CurrentUser.OpenSubKey($@"Software\Classes\CLSID\{clsid}\LocalServer32");
        var command = (string)server!.GetValue(null)!;
        command.Should().Contain(Path.GetFileName(Environment.ProcessPath!), "Windows must be able to start this very process");
    }

    /// <summary>
    /// Reads AppKit's <c>NSApp</c> for real. The test host never creates an application, so the badge
    /// must decline with that reason, and not with "AppKit could not be loaded", which is what a
    /// wrong framework path or export name would give.
    /// </summary>
    [TestMethod]
    public async Task On_macOS_a_process_without_an_application_has_no_badge_and_says_why()
    {
        if (!OperatingSystem.IsMacOS())
            Assert.Inconclusive("macOS only.");

        await using var badge = await BadgeManager.CreateAsync(new AppIdentity("com.example.Hark", "Hark Test"));

        badge.IsSupported.Should().BeFalse();
        badge.UnsupportedReason.Should().Contain("no NSApplication");
    }

    /// <summary>
    /// Drives the real taskbar for a hidden window: COM creation of TaskbarList, HrInit, the overlay
    /// icon (made by GDI from the rendered pixels) and the progress calls, through the vtable slots.
    /// A wrong slot or signature fails as an HRESULT, or as a crash.
    /// </summary>
    [TestMethod]
    public async Task On_Windows_the_badge_reaches_the_real_taskbar()
    {
        if (!OperatingSystem.IsWindows())
            Assert.Inconclusive("Windows only.");
        if (OperatingSystem.IsWindows()) // always true here; the analyzer cannot see through Assert.Inconclusive
            await WindowsTaskbarRoundTripAsync();
    }

    [System.Runtime.Versioning.SupportedOSPlatform("windows")]
    private static async Task WindowsTaskbarRoundTripAsync()
    {
        var hicon = Hark.Windows.Taskbar.CreateIcon(Hark.Windows.BadgeRenderer.RenderCount(42, 24));
        try
        {
            Hark.Windows.Taskbar.DescribeIcon(hicon).Should().Be((24, 24, 32), "the icon must carry the rendered 32-bit pixels at their size");
        }
        finally
        {
            Hark.Windows.Taskbar.DestroyIcon(hicon);
        }

        var window = HiddenWindow.Create();
        try
        {
            await using var badge = await BadgeManager.CreateAsync(new AppIdentity("dev.brightshore.Hark.Tests", "Hark Tests"),
                new BadgeOptions { WindowHandle = () => window });
            badge.IsSupported.Should().BeTrue(badge.UnsupportedReason);

            await badge.SetCountAsync(7);
            await badge.SetCountAsync(150);
            await badge.SetProgressAsync(0.5);
            await badge.ClearAsync();
        }
        finally
        {
            HiddenWindow.Destroy(window);
        }
    }

    [System.Runtime.Versioning.SupportedOSPlatform("windows")]
    private static class HiddenWindow
    {
        [System.Runtime.InteropServices.DllImport("user32.dll", CharSet = System.Runtime.InteropServices.CharSet.Unicode, ExactSpelling = true)]
        private static extern nint CreateWindowExW(uint exStyle, string className, string windowName, uint style,
            int x, int y, int width, int height, nint parent, nint menu, nint instance, nint param);

        [System.Runtime.InteropServices.DllImport("user32.dll", ExactSpelling = true)]
        private static extern int DestroyWindow(nint window);

        /// <summary>A top-level window of the predefined STATIC class, never shown, so the desktop is left alone.</summary>
        public static nint Create()
        {
            var window = CreateWindowExW(0, "STATIC", "Hark badge test", 0, 0, 0, 100, 100, 0, 0, 0, 0);
            window.Should().NotBe(0, "CreateWindowEx must give a real window");
            return window;
        }

        public static void Destroy(nint window) => DestroyWindow(window);
    }

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
            ImagePath = SampleImage,
            Silent = true,
        });
        var closes = new List<NotificationClosedEventArgs>();
        manager.Closed += (_, e) => closes.Add(e);
        await manager.CloseAsync(handle);

        File.Exists(SampleImage).Should().BeTrue("the toast must have carried a real image file");
        if (OperatingSystem.IsWindows()) // always true here; the analyzer cannot see through Assert.Inconclusive
            AssertActivatorRegistered((Hark.Windows.WindowsNotificationManager)manager, "dev.brightshore.Hark.Tests");
        handle.Id.Should().Be("t:suite");
        closes.Should().ContainSingle().Which.Reason.Should().Be(NotificationCloseReason.ClosedByApp);
    }
}
