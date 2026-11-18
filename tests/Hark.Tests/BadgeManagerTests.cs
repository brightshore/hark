using AwesomeAssertions;
using Hark.Linux;
using Hark.MacOS;
using Hark.Windows;

namespace Hark.Tests;

[TestClass]
public sealed class BadgeManagerTests
{
    private static readonly AppIdentity Identity = new("com.example.Hark", "Hark Test");
    private const nint Window = 0x1234;

    // ---- macOS: the Dock tile's label ----

    [TestMethod]
    public async Task On_macOS_a_count_is_the_label_and_zero_clears_it()
    {
        var tile = new FakeDockTile();
        var badge = MacBadgeManager.Create(tile);

        await badge.SetCountAsync(3);
        await badge.SetCountAsync(1234);
        await badge.SetCountAsync(0);

        tile.Labels.Should().Equal("3", "1234", null);
        badge.Capabilities.Should().Be(BadgeCapabilities.Count | BadgeCapabilities.Text);
    }

    [TestMethod]
    public async Task On_macOS_text_is_the_label_and_empty_text_clears_it()
    {
        var tile = new FakeDockTile();
        var badge = MacBadgeManager.Create(tile);

        await badge.SetTextAsync("new");
        await badge.SetTextAsync("");
        await badge.SetTextAsync("!");
        await badge.SetTextAsync(null);
        await badge.ClearAsync();

        tile.Labels.Should().Equal("new", null, "!", null, null);
    }

    [TestMethod]
    public async Task On_macOS_progress_is_not_supported_and_says_so()
    {
        var tile = new FakeDockTile();
        var badge = MacBadgeManager.Create(tile);

        var act = () => badge.SetProgressAsync(0.5);

        (await act.Should().ThrowAsync<NotSupportedException>()).WithMessage("*Progress*Capabilities*");
        tile.Labels.Should().BeEmpty();
    }

    [TestMethod]
    public async Task A_negative_count_is_refused_before_anything_is_shown()
    {
        var tile = new FakeDockTile();
        var badge = MacBadgeManager.Create(tile);

        var act = () => badge.SetCountAsync(-1);

        await act.Should().ThrowAsync<ArgumentOutOfRangeException>();
        tile.Labels.Should().BeEmpty();
    }

    [TestMethod]
    public async Task A_disposed_manager_refuses_to_set_a_badge()
    {
        var badge = MacBadgeManager.Create(new FakeDockTile());
        await badge.DisposeAsync();

        var act = () => badge.SetCountAsync(1);

        await act.Should().ThrowAsync<ObjectDisposedException>();
    }

    // ---- Linux: the Unity LauncherEntry signal ----

    [TestMethod]
    public async Task On_Linux_the_launcher_entry_is_addressed_by_the_desktop_file()
    {
        var bus = new FakeLauncherEntryBus();
        var badge = LauncherEntryBadgeManager.Create(Identity, bus);

        await badge.SetCountAsync(5);

        bus.Updates.Should().ContainSingle().Which.AppUri.Should().Be("application://com.example.Hark.desktop");
        badge.Capabilities.Should().Be(BadgeCapabilities.Count | BadgeCapabilities.Progress);
    }

    [TestMethod]
    public async Task On_Linux_a_count_shows_and_zero_hides_it()
    {
        var bus = new FakeLauncherEntryBus();
        var badge = LauncherEntryBadgeManager.Create(Identity, bus);

        await badge.SetCountAsync(5);
        await badge.SetCountAsync(0);

        bus.Updates.Select(u => u.Update).Should().Equal(
            new LauncherEntryUpdate(Count: 5, CountVisible: true),
            new LauncherEntryUpdate(Count: 0, CountVisible: false));
    }

    [TestMethod]
    public async Task On_Linux_progress_shows_and_null_hides_it_without_touching_the_count()
    {
        var bus = new FakeLauncherEntryBus();
        var badge = LauncherEntryBadgeManager.Create(Identity, bus);

        await badge.SetProgressAsync(0.25);
        await badge.SetProgressAsync(null);

        bus.Updates.Select(u => u.Update).Should().Equal(
            new LauncherEntryUpdate(Progress: 0.25, ProgressVisible: true),
            new LauncherEntryUpdate(ProgressVisible: false));
    }

    [TestMethod]
    public async Task On_Linux_clear_hides_the_count_and_the_progress()
    {
        var bus = new FakeLauncherEntryBus();
        var badge = LauncherEntryBadgeManager.Create(Identity, bus);

        await badge.ClearAsync();

        bus.Updates.Should().ContainSingle().Which.Update.Should().Be(new LauncherEntryUpdate(Count: 0, CountVisible: false, ProgressVisible: false));
    }

    [TestMethod]
    public async Task On_Linux_text_is_not_supported()
    {
        var bus = new FakeLauncherEntryBus();
        var badge = LauncherEntryBadgeManager.Create(Identity, bus);

        var act = () => badge.SetTextAsync("new");

        await act.Should().ThrowAsync<NotSupportedException>();
        bus.Updates.Should().BeEmpty();
    }

    [TestMethod]
    public async Task Disposing_the_Linux_manager_closes_its_bus()
    {
        var bus = new FakeLauncherEntryBus();
        var badge = LauncherEntryBadgeManager.Create(Identity, bus);

        await badge.DisposeAsync();

        bus.Disposed.Should().BeTrue();
    }

    [TestMethod]
    public void The_launcher_entry_object_path_is_valid_and_stable()
    {
        var path = DBusLauncherEntryBus.ObjectPath("application://com.example.Hark.desktop");

        path.Should().MatchRegex("^/com/canonical/unity/launcherentry/[0-9]+$");
        DBusLauncherEntryBus.ObjectPath("application://com.example.Hark.desktop").Should().Be(path);
        DBusLauncherEntryBus.ObjectPath("application://com.example.Other.desktop").Should().NotBe(path);
    }

    // ---- Progress bounds, shared by every backend ----

    [TestMethod]
    [DataRow(0.0)]
    [DataRow(1.0)]
    public async Task Progress_accepts_both_ends(double progress)
    {
        var bus = new FakeLauncherEntryBus();
        var badge = LauncherEntryBadgeManager.Create(Identity, bus);

        await badge.SetProgressAsync(progress);

        bus.Updates.Should().ContainSingle().Which.Update.Progress.Should().Be(progress);
    }

    [TestMethod]
    [DataRow(-0.01)]
    [DataRow(1.01)]
    [DataRow(double.NaN)]
    [DataRow(double.PositiveInfinity)]
    public async Task Progress_outside_zero_to_one_is_refused(double progress)
    {
        var bus = new FakeLauncherEntryBus();
        var badge = LauncherEntryBadgeManager.Create(Identity, bus);

        var act = () => badge.SetProgressAsync(progress);

        await act.Should().ThrowAsync<ArgumentOutOfRangeException>();
        bus.Updates.Should().BeEmpty();
    }

    // ---- Windows: the taskbar overlay ----

    [TestMethod]
    public async Task On_Windows_a_count_is_drawn_at_the_windows_overlay_size()
    {
        var taskbar = new FakeTaskbar(overlaySize: 24);
        var badge = WindowsBadgeManager.Create(taskbar, () => Window);

        await badge.SetCountAsync(7);

        var overlay = taskbar.Overlays.Should().ContainSingle().Subject;
        overlay.Window.Should().Be(Window);
        overlay.Icon!.Size.Should().Be(24);
        overlay.Icon.Pixels.Should().Equal(BadgeRenderer.RenderCount(7, 24).Pixels);
        overlay.Description.Should().Be("7");
        badge.Capabilities.Should().Be(BadgeCapabilities.Count | BadgeCapabilities.Progress);
    }

    [TestMethod]
    public async Task On_Windows_zero_removes_the_overlay()
    {
        var taskbar = new FakeTaskbar();
        var badge = WindowsBadgeManager.Create(taskbar, () => Window);

        await badge.SetCountAsync(0);

        taskbar.Overlays.Should().Equal((Window, (BadgeIcon?)null, (string?)null));
    }

    [TestMethod]
    public async Task On_Windows_progress_goes_to_the_taskbar_button()
    {
        var taskbar = new FakeTaskbar();
        var badge = WindowsBadgeManager.Create(taskbar, () => Window);

        await badge.SetProgressAsync(0.5);
        await badge.SetProgressAsync(null);

        taskbar.Progress.Should().Equal((Window, (double?)0.5), (Window, (double?)null));
    }

    [TestMethod]
    public async Task On_Windows_without_a_window_setting_a_badge_says_why()
    {
        var taskbar = new FakeTaskbar();
        var badge = WindowsBadgeManager.Create(taskbar, () => 0);

        var count = () => badge.SetCountAsync(3);
        var progress = () => badge.SetProgressAsync(0.5);

        (await count.Should().ThrowAsync<InvalidOperationException>()).WithMessage("*no window*");
        await progress.Should().ThrowAsync<InvalidOperationException>();
        taskbar.Overlays.Should().BeEmpty();
    }

    [TestMethod]
    public async Task On_Windows_clear_removes_both_and_is_a_no_op_without_a_window()
    {
        var taskbar = new FakeTaskbar();
        var window = Window;
        var badge = WindowsBadgeManager.Create(taskbar, () => window);

        await badge.ClearAsync();
        window = 0;
        await badge.ClearAsync();

        taskbar.Overlays.Should().Equal((Window, (BadgeIcon?)null, (string?)null));
        taskbar.Progress.Should().Equal((Window, (double?)null));
    }

    [TestMethod]
    public async Task On_Windows_text_is_not_supported()
    {
        var taskbar = new FakeTaskbar();
        var badge = WindowsBadgeManager.Create(taskbar, () => Window);

        var act = () => badge.SetTextAsync("new");

        await act.Should().ThrowAsync<NotSupportedException>();
        taskbar.Overlays.Should().BeEmpty();
    }

    [TestMethod]
    public async Task The_window_is_asked_for_on_every_change()
    {
        var taskbar = new FakeTaskbar();
        var window = (nint)1;
        var badge = WindowsBadgeManager.Create(taskbar, () => window);

        await badge.SetCountAsync(1);
        window = 2;
        await badge.SetCountAsync(2);

        taskbar.Overlays.Select(o => o.Window).Should().Equal(1, 2);
    }

    // ---- No badge at all ----

    [TestMethod]
    public async Task An_unsupported_badge_refuses_with_its_reason_and_clears_quietly()
    {
        IBadgeManager badge = new UnsupportedBadgeManager("no dock here");

        var act = () => badge.SetCountAsync(1);

        (await act.Should().ThrowAsync<InvalidOperationException>()).WithMessage("*no dock here*");
        await badge.ClearAsync();
        badge.IsSupported.Should().BeFalse();
        badge.Capabilities.Should().Be(BadgeCapabilities.None);
    }

    [TestMethod]
    public async Task A_backend_that_fails_unexpectedly_gives_an_unsupported_badge_that_says_why()
    {
        var badge = await BadgeManager.CreateGuardedAsync(() => throw new System.Net.Sockets.SocketException(111), CancellationToken.None);

        badge.IsSupported.Should().BeFalse();
        badge.UnsupportedReason.Should().Contain("SocketException");
    }

    [TestMethod]
    public async Task Cancelling_badge_creation_is_still_cancellation()
    {
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        var act = () => BadgeManager.CreateGuardedAsync(() => Task.FromCanceled<IBadgeManager>(cts.Token), cts.Token);

        await act.Should().ThrowAsync<OperationCanceledException>();
    }
}
