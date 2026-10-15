using AwesomeAssertions;
using Hark.MacOS;
using Microsoft.Extensions.Time.Testing;

namespace Hark.Tests;

[TestClass]
public sealed class MacNotificationManagerTests
{
    private static async Task<(INotificationManager Manager, FakeUserNotificationCenter Center)> CreateAsync()
    {
        var center = new FakeUserNotificationCenter();
        var manager = await MacNotificationManager.CreateAsync(center, CancellationToken.None);
        return (manager, center);
    }

    [TestMethod]
    public async Task A_user_who_said_no_gets_an_unsupported_manager_that_says_where_to_fix_it()
    {
        var center = new FakeUserNotificationCenter { Authorization = (false, null) };

        var manager = await MacNotificationManager.CreateAsync(center, CancellationToken.None);

        manager.IsSupported.Should().BeFalse();
        manager.UnsupportedReason.Should().Contain("System Settings");
    }

    [TestMethod]
    public async Task An_os_refusal_carries_the_os_explanation()
    {
        var center = new FakeUserNotificationCenter { Authorization = (false, "Notifications are not allowed for this application") };

        var manager = await MacNotificationManager.CreateAsync(center, CancellationToken.None);

        manager.UnsupportedReason.Should().Contain("Notifications are not allowed for this application")
            .And.Contain("System Settings", "macOS never prompts again, so the reason must say where to fix it");
    }

    [TestMethod]
    public async Task Sends_content_and_maps_urgency_and_silence()
    {
        var (manager, center) = await CreateAsync();

        await manager.ShowAsync(new Notification { Title = "Low", Urgency = NotificationUrgency.Low, Silent = true, ImagePath = "/tmp/a.png" });
        await manager.ShowAsync(new Notification { Title = "Normal", Body = "b" });
        await manager.ShowAsync(new Notification { Title = "Critical", Urgency = NotificationUrgency.Critical });

        center.Added.Select(r => (r.Title, r.Body, r.PlaySound, r.InterruptionLevel, r.ImagePath)).Should().Equal(
            ("Low", "", false, MacInterruptionLevel.Passive, "/tmp/a.png"),
            ("Normal", "b", true, MacInterruptionLevel.Active, (string?)null),
            ("Critical", "", true, MacInterruptionLevel.TimeSensitive, (string?)null));
    }

    [TestMethod]
    public async Task A_tag_is_the_request_identifier_so_the_os_replaces_in_place()
    {
        var (manager, center) = await CreateAsync();

        var first = await manager.ShowAsync(new Notification { Title = "1", Tag = "inbox" });
        var second = await manager.ShowAsync(new Notification { Title = "2", Tag = "inbox" });
        var untagged = await manager.ShowAsync(new Notification { Title = "3" });

        first.Should().Be(second);
        center.Added.Select(r => r.Identifier).Should().Equal("tag:inbox", "tag:inbox", untagged.Id);
        untagged.Id.Should().NotStartWith("tag:");
    }

    [TestMethod]
    public async Task Each_distinct_button_set_registers_once_and_re_registers_the_whole_set()
    {
        var (manager, center) = await CreateAsync();

        await manager.ShowAsync(new Notification { Title = "a", Actions = [new("reply", "Reply")] });
        await manager.ShowAsync(new Notification { Title = "b", Actions = [new("reply", "Reply")] });
        await manager.ShowAsync(new Notification { Title = "c" });

        center.CategoryRegistrations.Should().HaveCount(2, "the repeated button set needs no new registration");
        center.CategoryRegistrations[1].Should().HaveCount(2, "the OS replaces the set, so it must hold both");
        center.Added[0].CategoryIdentifier.Should().Be(center.Added[1].CategoryIdentifier);
        center.Added[2].CategoryIdentifier.Should().NotBe(center.Added[0].CategoryIdentifier);
        center.CategoryRegistrations[1].Single(c => c.Identifier == center.Added[0].CategoryIdentifier)
            .Actions.Should().Equal(new NotificationAction("reply", "Reply"));
    }

    [TestMethod]
    public void Category_ids_differ_when_only_a_label_differs()
    {
        MacNotificationManager.CategoryIdentifier([new("a", "Yes")])
            .Should().NotBe(MacNotificationManager.CategoryIdentifier([new("a", "No")]));
    }

    [TestMethod]
    public async Task Body_click_and_buttons_activate_once()
    {
        var (manager, center) = await CreateAsync();
        var body = await manager.ShowAsync(new Notification { Title = "t" });
        var button = await manager.ShowAsync(new Notification { Title = "t", Actions = [new("reply", "Reply")] });
        var activations = new List<NotificationActivatedEventArgs>();
        manager.Activated += (_, e) => activations.Add(e);

        center.Sink!.OnResponse(body.Id, MacNotificationManager.DefaultActionIdentifier);
        center.Sink.OnResponse(button.Id, "reply");
        center.Sink.OnResponse(button.Id, "reply");

        activations.Select(a => (a.Handle, a.ActionId, a.ActivationToken))
            .Should().Equal((body, (string?)null, (string?)null), (button, "reply", (string?)null));
    }

    [TestMethod]
    public async Task A_dismissal_closes_and_responses_for_other_notifications_are_ignored()
    {
        var (manager, center) = await CreateAsync();
        var handle = await manager.ShowAsync(new Notification { Title = "t" });
        var closes = new List<NotificationClosedEventArgs>();
        var activations = 0;
        manager.Closed += (_, e) => closes.Add(e);
        manager.Activated += (_, _) => activations++;

        center.Sink!.OnResponse("left-over-from-a-previous-run", MacNotificationManager.DefaultActionIdentifier);
        center.Sink.OnResponse(handle.Id, MacNotificationManager.DismissActionIdentifier);

        activations.Should().Be(0);
        closes.Should().ContainSingle().Which.Reason.Should().Be(NotificationCloseReason.Dismissed);
    }

    [TestMethod]
    public async Task Close_removes_and_reports_once()
    {
        var (manager, center) = await CreateAsync();
        var handle = await manager.ShowAsync(new Notification { Title = "t" });
        var closes = new List<NotificationClosedEventArgs>();
        manager.Closed += (_, e) => closes.Add(e);

        await manager.CloseAsync(handle);
        await manager.CloseAsync(handle);

        center.Removed.Should().Equal(handle.Id, handle.Id);
        closes.Should().ContainSingle().Which.Reason.Should().Be(NotificationCloseReason.ClosedByApp);
    }

    [TestMethod]
    public async Task Expiration_removes_the_notification_and_closes_it_as_expired()
    {
        var time = new FakeTimeProvider();
        var center = new FakeUserNotificationCenter();
        var manager = await MacNotificationManager.CreateAsync(center, CancellationToken.None, time);
        var expiring = await manager.ShowAsync(new Notification { Title = "t", Expiration = TimeSpan.FromSeconds(30) });
        var forever = await manager.ShowAsync(new Notification { Title = "t", Expiration = Timeout.InfiniteTimeSpan });
        var closes = new List<NotificationClosedEventArgs>();
        manager.Closed += (_, e) => closes.Add(e);

        time.Advance(TimeSpan.FromSeconds(29));
        closes.Should().BeEmpty();
        time.Advance(TimeSpan.FromHours(1));

        center.Removed.Should().Equal(expiring.Id);
        closes.Should().ContainSingle().Which.Should().BeEquivalentTo(new { Handle = expiring, Reason = NotificationCloseReason.Expired });
        forever.Id.Should().NotBe(expiring.Id);
    }

    [TestMethod]
    public async Task A_click_or_a_replacement_without_expiry_cancels_the_timer()
    {
        var time = new FakeTimeProvider();
        var center = new FakeUserNotificationCenter();
        var manager = await MacNotificationManager.CreateAsync(center, CancellationToken.None, time);
        var clicked = await manager.ShowAsync(new Notification { Title = "t", Expiration = TimeSpan.FromSeconds(5) });
        await manager.ShowAsync(new Notification { Title = "t", Tag = "x", Expiration = TimeSpan.FromSeconds(5) });
        await manager.ShowAsync(new Notification { Title = "t", Tag = "x" });
        var closes = new List<NotificationClosedEventArgs>();
        manager.Closed += (_, e) => closes.Add(e);

        center.Sink!.OnResponse(clicked.Id, MacNotificationManager.DefaultActionIdentifier);
        time.Advance(TimeSpan.FromMinutes(1));

        closes.Should().BeEmpty();
        center.Removed.Should().BeEmpty();
    }

    [TestMethod]
    public async Task Applies_the_same_validation_as_every_backend()
    {
        var (manager, center) = await CreateAsync();

        var act = () => manager.ShowAsync(new Notification { Title = "t", Actions = [new("default", "Hijack")] });

        await act.Should().ThrowAsync<ArgumentException>();
        center.Added.Should().BeEmpty();
    }
}
