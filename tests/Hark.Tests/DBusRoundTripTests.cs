using AwesomeAssertions;
using Hark.Linux;
using Tmds.DBus.Protocol;

namespace Hark.Tests;

/// <summary>
/// The one test that carries the real dependency: <see cref="DBusNotificationsBus"/> against a live
/// session bus, with a stand-in notification server owning <c>org.freedesktop.Notifications</c> on it.
/// It proves the wire signatures, the hint encoding and the signal subscriptions that the in-memory
/// tests take on trust.
/// </summary>
/// <remarks>
/// Needs a session bus nobody else serves notifications on, so run it under <c>dbus-run-session</c>.
/// CI sets <c>HARK_REQUIRE_DBUS=1</c>, which turns a missing bus from "inconclusive" into a failure:
/// a skipped proof must never read as a passing one.
/// </remarks>
[TestClass]
public sealed class DBusRoundTripTests
{
    private static readonly TimeSpan Wait = TimeSpan.FromSeconds(10);

    [TestMethod]
    public async Task Show_activate_and_close_round_trip_over_a_real_bus()
    {
        var address = RequireSessionBus();

        using var serverConnection = new DBusConnection(address);
        await serverConnection.ConnectAsync();
        var server = new StandInServer(serverConnection);
        serverConnection.AddMethodHandler(server);
        await serverConnection.RequestNameAsync(DBusNotificationsBus.ServiceName);

        var bus = await DBusNotificationsBus.ConnectAsync(address, CancellationToken.None);
        await using var manager = await FreeDesktopNotificationManager.CreateAsync(
            new AppIdentity("com.example.Hark", "Hark Test"), bus, CancellationToken.None);

        manager.Capabilities.Should().HaveFlag(NotificationCapabilities.Actions);

        var handle = await manager.ShowAsync(new Notification
        {
            Title = "Deploy done",
            Body = "<b>3</b> services",
            ImagePath = "/tmp/hark.png",
            Urgency = NotificationUrgency.Low,
            Silent = true,
            Expiration = TimeSpan.FromSeconds(2),
            Actions = [new("view", "View")],
        });

        handle.Id.Should().Be(StandInServer.AssignedId.ToString());
        var call = server.LastNotify!;
        call.AppName.Should().Be("Hark Test");
        call.Summary.Should().Be("Deploy done");
        call.Body.Should().Be("&lt;b&gt;3&lt;/b&gt; services", "the stand-in advertises body-markup");
        call.Actions.Should().Equal("default", "Open", "view", "View");
        call.ExpireTimeout.Should().Be(2000);
        call.Hints.Should().BeEquivalentTo(new NotifyHints(0, "com.example.Hark", "/tmp/hark.png", SuppressSound: true));

        var activated = new TaskCompletionSource<NotificationActivatedEventArgs>(TaskCreationOptions.RunContinuationsAsynchronously);
        manager.Activated += (_, e) => activated.TrySetResult(e);
        server.EmitActivation(StandInServer.AssignedId, "view", "xdg-token");
        var activation = await activated.Task.WaitAsync(Wait);
        activation.ActionId.Should().Be("view");
        activation.ActivationToken.Should().Be("xdg-token");

        var closed = new TaskCompletionSource<NotificationClosedEventArgs>(TaskCreationOptions.RunContinuationsAsynchronously);
        manager.Closed += (_, e) => closed.TrySetResult(e);
        await manager.CloseAsync(handle);
        var close = await closed.Task.WaitAsync(Wait);
        close.Reason.Should().Be(NotificationCloseReason.ClosedByApp);
        server.ClosedIds.Should().Equal(StandInServer.AssignedId);

        // The server has forgotten it now; closing again must be the documented no-op.
        await manager.CloseAsync(handle);
    }

    private static string RequireSessionBus()
    {
        var address = OperatingSystem.IsLinux() ? DBusAddress.Session : null;
        if (!string.IsNullOrEmpty(address))
            return address;

        const string reason = "No D-Bus session bus on this machine; run under dbus-run-session on Linux.";
        if (Environment.GetEnvironmentVariable("HARK_REQUIRE_DBUS") == "1")
            Assert.Fail(reason);
        Assert.Inconclusive(reason);
        return null!;
    }

    private sealed record NotifyCall(string AppName, string Summary, string Body, string[] Actions, NotifyHints Hints, int ExpireTimeout);

    private delegate void WriteBody(ref MessageWriter writer);

    /// <summary>Just enough of a notification daemon to answer Hark and emit its signals.</summary>
    private sealed class StandInServer(DBusConnection connection) : IPathMethodHandler
    {
        public const uint AssignedId = 42;

        private readonly HashSet<uint> _open = [];

        public string Path => DBusNotificationsBus.ObjectPath;
        public bool HandlesChildPaths => false;
        public NotifyCall? LastNotify { get; private set; }
        public List<uint> ClosedIds { get; } = [];

        public ValueTask HandleMethodAsync(MethodContext context)
        {
            var request = context.Request;
            if (request.InterfaceAsString != DBusNotificationsBus.InterfaceName)
            {
                context.ReplyUnknownMethodError();
                return default;
            }

            switch (request.MemberAsString)
            {
                case "GetCapabilities":
                    Reply(context, "as", (ref MessageWriter w) => w.WriteArray(new[] { "actions", "body", "body-markup" }));
                    break;

                case "Notify":
                {
                    var reader = request.GetBodyReader();
                    var appName = reader.ReadString();
                    reader.ReadUInt32();
                    reader.ReadString();
                    var summary = reader.ReadString();
                    var body = reader.ReadString();
                    var actions = reader.ReadArrayOfString();
                    var hints = reader.ReadDictionaryOfStringToVariantValue();
                    var timeout = reader.ReadInt32();
                    LastNotify = new NotifyCall(appName, summary, body, actions, new NotifyHints(
                        hints["urgency"].GetByte(),
                        hints.TryGetValue("desktop-entry", out var entry) ? entry.GetString() : null,
                        hints.TryGetValue("image-path", out var image) ? image.GetString() : null,
                        hints.TryGetValue("suppress-sound", out var silent) && silent.GetBool()), timeout);
                    _open.Add(AssignedId);
                    Reply(context, "u", (ref MessageWriter w) => w.WriteUInt32(AssignedId));
                    break;
                }

                case "CloseNotification":
                {
                    var reader = request.GetBodyReader();
                    var id = reader.ReadUInt32();
                    if (!_open.Remove(id))
                    {
                        // What the spec prescribes for an id it no longer has.
                        context.ReplyError("org.freedesktop.DBus.Error.Failed", "No such notification");
                        break;
                    }
                    ClosedIds.Add(id);
                    Reply(context, null, (ref MessageWriter _) => { });
                    Emit("NotificationClosed", "uu", (ref MessageWriter w) => { w.WriteUInt32(id); w.WriteUInt32(3); });
                    break;
                }

                default:
                    context.ReplyUnknownMethodError();
                    break;
            }
            return default;
        }

        public void EmitActivation(uint id, string actionKey, string token)
        {
            Emit("ActivationToken", "us", (ref MessageWriter w) => { w.WriteUInt32(id); w.WriteString(token); });
            Emit("ActionInvoked", "us", (ref MessageWriter w) => { w.WriteUInt32(id); w.WriteString(actionKey); });
        }

        private static void Reply(MethodContext context, string? signature, WriteBody body)
        {
            var writer = context.CreateReplyWriter(signature!);
            try
            {
                body(ref writer);
                context.Reply(writer.CreateMessage());
            }
            finally
            {
                writer.Dispose();
            }
        }

        private void Emit(string member, string signature, WriteBody body)
        {
            var writer = connection.GetMessageWriter();
            try
            {
                writer.WriteSignalHeader(null, DBusNotificationsBus.ObjectPath, DBusNotificationsBus.InterfaceName, member, signature);
                body(ref writer);
                connection.TrySendMessage(writer.CreateMessage()).Should().BeTrue();
            }
            finally
            {
                writer.Dispose();
            }
        }
    }
}
