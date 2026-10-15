using AwesomeAssertions;
using Hark.Linux;
using Tmds.DBus.Protocol;

namespace Hark.Tests;

[TestClass]
public sealed class FreeDesktopNotificationManagerTests
{
    private static readonly AppIdentity Identity = new("com.example.Hark", "Hark Test") { IconPath = "/usr/share/icons/hark.png" };

    private static async Task<(FreeDesktopNotificationManager Manager, FakeNotificationsBus Bus)> CreateAsync(params string[] serverCapabilities)
    {
        var bus = new FakeNotificationsBus(serverCapabilities);
        var manager = await FreeDesktopNotificationManager.CreateAsync(Identity, bus, CancellationToken.None);
        return (manager, bus);
    }

    [TestMethod]
    public async Task Subscribes_before_anything_can_be_shown()
    {
        var (_, bus) = await CreateAsync();

        bus.Sink.Should().NotBeNull();
    }

    [TestMethod]
    public async Task Maps_server_capabilities()
    {
        var (bare, _) = await CreateAsync();
        var (full, _) = await CreateAsync("actions", "body-markup", "icon-static", "sound", "persistence");

        bare.Capabilities.Should().Be(NotificationCapabilities.Show | NotificationCapabilities.Close);
        full.Capabilities.Should().Be(
            NotificationCapabilities.Show | NotificationCapabilities.Close | NotificationCapabilities.Actions
            | NotificationCapabilities.BodyActivation | NotificationCapabilities.Images
            | NotificationCapabilities.Sound | NotificationCapabilities.Persistence);
    }

    [TestMethod]
    public async Task Sends_identity_content_and_hints()
    {
        var (manager, bus) = await CreateAsync();

        await manager.ShowAsync(new Notification
        {
            Title = "Build finished",
            Body = "All green",
            ImagePath = "/tmp/shot.png",
            Urgency = NotificationUrgency.Critical,
            Silent = true,
            Expiration = TimeSpan.FromSeconds(5),
        });

        bus.Sent.Should().ContainSingle().Which.Should().BeEquivalentTo(new NotifyRequest(
            AppName: "Hark Test",
            ReplacesId: 0,
            AppIcon: "/usr/share/icons/hark.png",
            Summary: "Build finished",
            Body: "All green",
            Actions: [],
            Hints: new NotifyHints(Urgency: 2, DesktopEntry: "com.example.Hark", ImagePath: "/tmp/shot.png", SuppressSound: true),
            ExpireTimeout: 5000));
    }

    [TestMethod]
    public async Task Flattens_actions_with_default_first_when_the_server_supports_them()
    {
        var (manager, bus) = await CreateAsync("actions");

        await manager.ShowAsync(new Notification
        {
            Title = "Message",
            Actions = [new("reply", "Reply"), new("mute", "Mute")],
        });

        bus.Sent[0].Actions.Should().Equal("default", "Open", "reply", "Reply", "mute", "Mute");
    }

    [TestMethod]
    public async Task Sends_no_actions_to_a_server_without_them()
    {
        var (manager, bus) = await CreateAsync();

        await manager.ShowAsync(new Notification { Title = "Message", Actions = [new("reply", "Reply")] });

        bus.Sent[0].Actions.Should().BeEmpty();
    }

    [TestMethod]
    public async Task Escapes_the_body_only_for_a_markup_server()
    {
        var (plain, plainBus) = await CreateAsync();
        var (markup, markupBus) = await CreateAsync("body-markup");
        var notification = new Notification { Title = "t", Body = "a < b && c > \"d\"" };

        await plain.ShowAsync(notification);
        await markup.ShowAsync(notification);

        plainBus.Sent[0].Body.Should().Be("a < b && c > \"d\"");
        markupBus.Sent[0].Body.Should().Be("a &lt; b &amp;&amp; c &gt; \"d\"");
    }

    [TestMethod]
    public async Task A_tag_replaces_the_notification_on_screen_until_it_closes()
    {
        var (manager, bus) = await CreateAsync();

        var first = await manager.ShowAsync(new Notification { Title = "1 new", Tag = "inbox" });
        var second = await manager.ShowAsync(new Notification { Title = "2 new", Tag = "inbox" });
        bus.Sink!.OnNotificationClosed(uint.Parse(second.Id), 2);
        var third = await manager.ShowAsync(new Notification { Title = "3 new", Tag = "inbox" });

        bus.Sent.Select(r => r.ReplacesId).Should().Equal(0u, uint.Parse(first.Id), 0u);
        second.Should().Be(first);
        third.Id.Should().NotBe(first.Id);
    }

    [TestMethod]
    public async Task Body_click_activates_with_no_action_id_and_carries_the_activation_token()
    {
        var (manager, bus) = await CreateAsync("actions");
        var handle = await manager.ShowAsync(new Notification { Title = "t" });
        NotificationActivatedEventArgs? activated = null;
        manager.Activated += (_, e) => activated = e;

        bus.Sink!.OnActivationToken(uint.Parse(handle.Id), "xdg-token");
        bus.Sink.OnActionInvoked(uint.Parse(handle.Id), "default");

        activated.Should().NotBeNull();
        activated!.Handle.Should().Be(handle);
        activated.ActionId.Should().BeNull();
        activated.ActivationToken.Should().Be("xdg-token");
    }

    [TestMethod]
    public async Task A_button_activates_with_its_id_and_a_token_is_used_once()
    {
        var (manager, bus) = await CreateAsync("actions");
        var handle = await manager.ShowAsync(new Notification { Title = "t", Actions = [new("reply", "Reply")] });
        var activations = new List<NotificationActivatedEventArgs>();
        manager.Activated += (_, e) => activations.Add(e);
        var id = uint.Parse(handle.Id);

        bus.Sink!.OnActivationToken(id, "first");
        bus.Sink.OnActionInvoked(id, "reply");
        bus.Sink.OnActionInvoked(id, "reply");

        activations.Select(a => (a.ActionId, a.ActivationToken)).Should().Equal(("reply", "first"), ("reply", (string?)null));
    }

    [TestMethod]
    public async Task Ignores_signals_for_notifications_it_did_not_show()
    {
        var (manager, bus) = await CreateAsync("actions");
        var raised = 0;
        manager.Activated += (_, _) => raised++;
        manager.Closed += (_, _) => raised++;

        // Another app's notification on the same daemon.
        bus.Sink!.OnActivationToken(7, "theirs");
        bus.Sink.OnActionInvoked(7, "default");
        bus.Sink.OnNotificationClosed(7, 2);

        raised.Should().Be(0);
    }

    [TestMethod]
    [DataRow(1u, NotificationCloseReason.Expired)]
    [DataRow(2u, NotificationCloseReason.Dismissed)]
    [DataRow(3u, NotificationCloseReason.ClosedByApp)]
    [DataRow(4u, NotificationCloseReason.Unknown)]
    [DataRow(99u, NotificationCloseReason.Unknown)]
    public async Task Maps_close_reasons_and_reports_each_close_once(uint wireReason, NotificationCloseReason expected)
    {
        var (manager, bus) = await CreateAsync();
        var handle = await manager.ShowAsync(new Notification { Title = "t" });
        var closes = new List<NotificationClosedEventArgs>();
        manager.Closed += (_, e) => closes.Add(e);

        bus.Sink!.OnNotificationClosed(uint.Parse(handle.Id), wireReason);
        bus.Sink.OnNotificationClosed(uint.Parse(handle.Id), wireReason);

        closes.Should().ContainSingle().Which.Reason.Should().Be(expected);
        closes[0].Handle.Should().Be(handle);
    }

    [TestMethod]
    public async Task Closing_a_notification_the_server_no_longer_has_is_a_no_op()
    {
        var (manager, bus) = await CreateAsync();
        var handle = await manager.ShowAsync(new Notification { Title = "t" });
        bus.CloseFailure = new DBusErrorReplyException("org.freedesktop.DBus.Error.Failed", "");

        await manager.CloseAsync(handle);

        bus.CloseCalls.Should().Equal(uint.Parse(handle.Id));
    }

    [TestMethod]
    public async Task Other_close_failures_propagate()
    {
        var (manager, bus) = await CreateAsync();
        var handle = await manager.ShowAsync(new Notification { Title = "t" });
        bus.CloseFailure = new DBusConnectionClosedException("gone");

        var act = () => manager.CloseAsync(handle);

        await act.Should().ThrowAsync<DBusConnectionClosedException>();
    }

    [TestMethod]
    public async Task Rejects_invalid_notifications_without_sending()
    {
        var (manager, bus) = await CreateAsync("actions");

        Notification[] invalid =
        [
            new() { Title = " " },
            new() { Title = "t", Actions = [new("default", "Hijack")] },
            new() { Title = "t", Actions = [new("a", "One"), new("a", "Two")] },
            new() { Title = "t", Actions = [new("", "Empty")] },
            new() { Title = "t", Expiration = TimeSpan.Zero },
        ];
        foreach (var notification in invalid)
        {
            var act = () => manager.ShowAsync(notification);
            await act.Should().ThrowAsync<ArgumentException>();
        }

        bus.Sent.Should().BeEmpty();
    }

    [TestMethod]
    public void Expiration_maps_to_the_spec_timeout()
    {
        FreeDesktopNotificationManager.ToExpireTimeout(null).Should().Be(-1);
        FreeDesktopNotificationManager.ToExpireTimeout(Timeout.InfiniteTimeSpan).Should().Be(0);
        FreeDesktopNotificationManager.ToExpireTimeout(TimeSpan.FromSeconds(3)).Should().Be(3000);
        // Must not round down to 0, which the spec reads as "never expire".
        FreeDesktopNotificationManager.ToExpireTimeout(TimeSpan.FromTicks(1)).Should().Be(1);
        FreeDesktopNotificationManager.ToExpireTimeout(TimeSpan.FromDays(100)).Should().Be(int.MaxValue);
    }

    [TestMethod]
    public async Task Dispose_releases_the_bus_and_refuses_further_use()
    {
        var (manager, bus) = await CreateAsync();

        await manager.DisposeAsync();
        var act = () => manager.ShowAsync(new Notification { Title = "t" });

        bus.Disposed.Should().BeTrue();
        await act.Should().ThrowAsync<ObjectDisposedException>();
    }
}
