using System.Security.Cryptography;
using System.Text;
using System.Xml.Linq;
using AwesomeAssertions;
using Hark.Windows;

namespace Hark.Tests;

[TestClass]
public sealed class WindowsNotificationManagerTests
{
    private static (WindowsNotificationManager Manager, FakeToastPlatform Platform) Create()
    {
        var platform = new FakeToastPlatform();
        return (WindowsNotificationManager.Create(platform), platform);
    }

    [TestMethod]
    public void Builds_a_generic_toast_with_title_body_image_and_the_launch_argument()
    {
        var image = Path.GetFullPath(Path.Combine(Path.GetTempPath(), "hark shot.png"));

        var xml = XElement.Parse(WindowsNotificationManager.BuildXml(new Notification
        {
            Title = "Build <done> & green",
            Body = "\"all\" passed",
            ImagePath = image,
        }, "t:build"));

        xml.Name.LocalName.Should().Be("toast");
        xml.Attribute("launch")!.Value.Should().Be(ToastArguments.Format("t:build", actionId: null));
        var binding = xml.Element("visual")!.Element("binding")!;
        binding.Attribute("template")!.Value.Should().Be("ToastGeneric");
        binding.Elements("text").Select(t => t.Value).Should().Equal("Build <done> & green", "\"all\" passed");
        binding.Element("image")!.Attribute("src")!.Value.Should().Be(new Uri(image).AbsoluteUri).And.StartWith("file:///");
        xml.Element("actions").Should().BeNull();
        xml.Element("audio").Should().BeNull();
        xml.Attribute("scenario").Should().BeNull();
    }

    [TestMethod]
    public void Omits_an_empty_body_and_maps_buttons_silence_and_critical()
    {
        var xml = XElement.Parse(WindowsNotificationManager.BuildXml(new Notification
        {
            Title = "t",
            Actions = [new("reply", "Reply"), new("mute", "Mute")],
            Silent = true,
            Urgency = NotificationUrgency.Critical,
        }, "g:1"));

        xml.Element("visual")!.Element("binding")!.Elements("text").Should().ContainSingle();
        xml.Element("actions")!.Elements("action")
            .Select(a => (a.Attribute("content")!.Value, a.Attribute("arguments")!.Value, a.Attribute("activationType")!.Value))
            .Should().Equal(
                ("Reply", ToastArguments.Format("g:1", "reply"), "foreground"),
                ("Mute", ToastArguments.Format("g:1", "mute"), "foreground"));
        xml.Element("audio")!.Attribute("silent")!.Value.Should().Be("true");
        xml.Attribute("scenario")!.Value.Should().Be("urgent");
    }

    [TestMethod]
    public async Task Low_urgency_skips_the_popup_and_others_do_not()
    {
        var (manager, platform) = Create();

        await manager.ShowAsync(new Notification { Title = "quiet", Urgency = NotificationUrgency.Low });
        await manager.ShowAsync(new Notification { Title = "normal" });

        platform.Shown.Select(r => r.SuppressPopup).Should().Equal(true, false);
    }

    [TestMethod]
    public async Task Tags_map_to_stable_toast_tags_within_the_windows_limit()
    {
        var (manager, platform) = Create();
        var longTag = new string('x', 100);

        var first = await manager.ShowAsync(new Notification { Title = "1", Tag = "inbox" });
        var second = await manager.ShowAsync(new Notification { Title = "2", Tag = "inbox" });
        var untagged = await manager.ShowAsync(new Notification { Title = "3" });
        var hashed = await manager.ShowAsync(new Notification { Title = "4", Tag = longTag });

        first.Should().Be(second);
        first.Id.Should().Be("t:inbox");
        untagged.Id.Should().StartWith("g:");
        hashed.Id.Should().Be(WindowsNotificationManager.ToastTag(longTag), "the same long tag must replace in place too");
        hashed.Id.Should().Be("h:" + Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(longTag)))[..40]);
        platform.Shown.Should().OnlyContain(r => r.Tag.Length <= WindowsNotificationManager.MaxTagLength);
    }

    [TestMethod]
    public async Task Body_click_and_buttons_activate_once()
    {
        var (manager, platform) = Create();
        var body = await manager.ShowAsync(new Notification { Title = "t" });
        var button = await manager.ShowAsync(new Notification { Title = "t", Actions = [new("reply", "Reply")] });
        var activations = new List<NotificationActivatedEventArgs>();
        manager.Activated += (_, e) => activations.Add(e);

        platform.Sink!.OnActivated(ToastArguments.Format(body.Id, null), fromActivator: false);
        platform.Sink.OnActivated(ToastArguments.Format(button.Id, "reply"), fromActivator: true);
        platform.Sink.OnActivated(ToastArguments.Format(button.Id, "reply"), fromActivator: false);

        activations.Select(a => (a.Handle, a.ActionId)).Should().Equal((body, (string?)null), (button, "reply"));
    }

    [TestMethod]
    public async Task Timing_out_into_notification_center_is_not_a_close()
    {
        var (manager, platform) = Create();
        var handle = await manager.ShowAsync(new Notification { Title = "t" });
        var closes = new List<NotificationClosedEventArgs>();
        var activations = new List<NotificationActivatedEventArgs>();
        manager.Closed += (_, e) => closes.Add(e);
        manager.Activated += (_, e) => activations.Add(e);

        platform.Sink!.OnDismissed(handle.Id, 2);
        platform.Sink.OnActivated(ToastArguments.Format(handle.Id, null), fromActivator: true);

        closes.Should().BeEmpty();
        activations.Should().ContainSingle("a click from Notification Center still counts");
    }

    [TestMethod]
    [DataRow(0, NotificationCloseReason.Dismissed)]
    [DataRow(1, NotificationCloseReason.ClosedByApp)]
    public async Task Dismissals_close_once(int reason, NotificationCloseReason expected)
    {
        var (manager, platform) = Create();
        var handle = await manager.ShowAsync(new Notification { Title = "t" });
        var closes = new List<NotificationClosedEventArgs>();
        manager.Closed += (_, e) => closes.Add(e);

        platform.Sink!.OnDismissed(handle.Id, reason);
        platform.Sink.OnDismissed(handle.Id, reason);

        closes.Should().ContainSingle().Which.Reason.Should().Be(expected);
    }

    [TestMethod]
    public async Task A_failed_toast_closes_with_an_unknown_reason()
    {
        var (manager, platform) = Create();
        var handle = await manager.ShowAsync(new Notification { Title = "t" });
        var closes = new List<NotificationClosedEventArgs>();
        manager.Closed += (_, e) => closes.Add(e);

        platform.Sink!.OnFailed(handle.Id, unchecked((int)0x80070490));

        closes.Should().ContainSingle().Which.Reason.Should().Be(NotificationCloseReason.Unknown);
    }

    [TestMethod]
    public async Task Close_hides_and_reports_once_and_events_for_unknown_tags_are_ignored()
    {
        var (manager, platform) = Create();
        var handle = await manager.ShowAsync(new Notification { Title = "t" });
        var raised = new List<EventArgs>();
        manager.Closed += (_, e) => raised.Add(e);
        manager.Activated += (_, e) => raised.Add(e);

        await manager.CloseAsync(handle);
        await manager.CloseAsync(handle);
        platform.Sink!.OnDismissed(handle.Id, 1);
        platform.Sink.OnActivated(ToastArguments.Format("g:not-ours", null), fromActivator: false);

        platform.Hidden.Should().Equal(handle.Id, handle.Id);
        raised.Should().ContainSingle().Which.Should().BeOfType<NotificationClosedEventArgs>()
            .Which.Reason.Should().Be(NotificationCloseReason.ClosedByApp);
    }

    [TestMethod]
    public void The_activator_reports_toasts_from_an_earlier_run_with_the_handle_that_run_had()
    {
        var (manager, platform) = Create();
        var activations = new List<NotificationActivatedEventArgs>();
        manager.Activated += (_, e) => activations.Add(e);

        platform.Sink!.OnActivated(ToastArguments.Format("t:inbox", "reply"), fromActivator: true);
        platform.Sink.OnActivated(ToastArguments.Format("g:0123", null), fromActivator: true);
        // The in-process event never reports what this process did not show.
        platform.Sink.OnActivated(ToastArguments.Format("g:4567", null), fromActivator: false);
        // Arguments Hark did not write are not its toast.
        platform.Sink.OnActivated("someone-elses-arguments", fromActivator: true);

        activations.Select(a => (a.Handle, a.ActionId)).Should().Equal(
            (new NotificationHandle("t:inbox", "inbox"), "reply"),
            (new NotificationHandle("g:0123", null), (string?)null));
    }

    [TestMethod]
    public void A_click_before_anyone_subscribes_is_delivered_to_the_first_subscriber_once()
    {
        var (manager, platform) = Create();

        // An app started by a toast click: the activator calls in during startup.
        platform.Sink!.OnActivated(ToastArguments.Format("t:launch", null), fromActivator: true);
        var first = new List<NotificationActivatedEventArgs>();
        var second = new List<NotificationActivatedEventArgs>();
        manager.Activated += (_, e) => first.Add(e);
        manager.Activated += (_, e) => second.Add(e);

        first.Should().ContainSingle().Which.Handle.Should().Be(new NotificationHandle("t:launch", "launch"));
        second.Should().BeEmpty();
    }

    [TestMethod]
    public async Task Applies_the_same_validation_as_every_backend()
    {
        var (manager, platform) = Create();

        var act = () => manager.ShowAsync(new Notification { Title = "t", Actions = [new("default", "Hijack")] });

        await act.Should().ThrowAsync<ArgumentException>();
        platform.Shown.Should().BeEmpty();
    }
}
