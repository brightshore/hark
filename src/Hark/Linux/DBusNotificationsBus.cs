using Tmds.DBus.Protocol;

namespace Hark.Linux;

/// <summary>
/// <see cref="INotificationsBus"/> over a real session bus connection, which this owns.
/// </summary>
/// <remarks>
/// Writers are disposed in <c>finally</c> rather than with <c>using var</c>: <see cref="MessageWriter"/>
/// is a mutable struct, and a <c>using</c> local is read-only, so every write would be made on a
/// defensive copy.
/// </remarks>
internal sealed class DBusNotificationsBus : INotificationsBus
{
    internal const string ServiceName = "org.freedesktop.Notifications";
    internal const string ObjectPath = "/org/freedesktop/Notifications";
    internal const string InterfaceName = "org.freedesktop.Notifications";

    private readonly DBusConnection _connection;
    private readonly List<IDisposable> _subscriptions = [];

    private DBusNotificationsBus(DBusConnection connection) => _connection = connection;

    public static async Task<DBusNotificationsBus> ConnectAsync(string address, CancellationToken cancellationToken)
    {
        var connection = new DBusConnection(address);
        try
        {
            await connection.ConnectAsync().AsTask().WaitAsync(cancellationToken).ConfigureAwait(false);
            return new DBusNotificationsBus(connection);
        }
        catch
        {
            connection.Dispose();
            throw;
        }
    }

    public Task<string[]> GetCapabilitiesAsync()
    {
        var writer = _connection.GetMessageWriter();
        MessageBuffer message;
        try
        {
            writer.WriteMethodCallHeader(ServiceName, ObjectPath, InterfaceName, "GetCapabilities");
            message = writer.CreateMessage();
        }
        finally
        {
            writer.Dispose();
        }

        return _connection.CallMethodAsync(message, static (Message reply, object? _) =>
        {
            var reader = reply.GetBodyReader();
            return reader.ReadArrayOfString();
        });
    }

    public Task<uint> NotifyAsync(NotifyRequest request)
    {
        var writer = _connection.GetMessageWriter();
        MessageBuffer message;
        try
        {
            writer.WriteMethodCallHeader(ServiceName, ObjectPath, InterfaceName, "Notify", "susssasa{sv}i");
            writer.WriteString(request.AppName);
            writer.WriteUInt32(request.ReplacesId);
            writer.WriteString(request.AppIcon);
            writer.WriteString(request.Summary);
            writer.WriteString(request.Body);
            writer.WriteArray(request.Actions);

            var hints = writer.WriteDictionaryStart();
            writer.WriteDictionaryEntryStart();
            writer.WriteString("urgency");
            writer.WriteVariantByte(request.Hints.Urgency);
            if (request.Hints.DesktopEntry is { } desktopEntry)
            {
                writer.WriteDictionaryEntryStart();
                writer.WriteString("desktop-entry");
                writer.WriteVariantString(desktopEntry);
            }
            if (request.Hints.ImagePath is { } imagePath)
            {
                writer.WriteDictionaryEntryStart();
                writer.WriteString("image-path");
                writer.WriteVariantString(imagePath);
            }
            if (request.Hints.SuppressSound)
            {
                writer.WriteDictionaryEntryStart();
                writer.WriteString("suppress-sound");
                writer.WriteVariantBool(true);
            }
            writer.WriteDictionaryEnd(hints);

            writer.WriteInt32(request.ExpireTimeout);
            message = writer.CreateMessage();
        }
        finally
        {
            writer.Dispose();
        }

        return _connection.CallMethodAsync(message, static (Message reply, object? _) =>
        {
            var reader = reply.GetBodyReader();
            return reader.ReadUInt32();
        });
    }

    public Task CloseNotificationAsync(uint id)
    {
        var writer = _connection.GetMessageWriter();
        MessageBuffer message;
        try
        {
            writer.WriteMethodCallHeader(ServiceName, ObjectPath, InterfaceName, "CloseNotification", "u");
            writer.WriteUInt32(id);
            message = writer.CreateMessage();
        }
        finally
        {
            writer.Dispose();
        }

        return _connection.CallMethodAsync(message);
    }

    public async Task SubscribeAsync(INotificationSignalSink sink)
    {
        // Sender is left open: a well-known name in a match rule only matches while that owner holds
        // it, and a daemon restart would silently end delivery. The manager ignores ids it never issued.
        _subscriptions.Add(await _connection.WatchSignalAsync(
            sender: null, ObjectPath, InterfaceName, "NotificationClosed",
            static (Message message, object? _) =>
            {
                var reader = message.GetBodyReader();
                return (Id: reader.ReadUInt32(), Reason: reader.ReadUInt32());
            },
            signal =>
            {
                if (signal.HasValue)
                    sink.OnNotificationClosed(signal.Value.Id, signal.Value.Reason);
            },
            ObserverFlags.None, emitOnCapturedContext: false).ConfigureAwait(false));

        _subscriptions.Add(await _connection.WatchSignalAsync(
            sender: null, ObjectPath, InterfaceName, "ActionInvoked",
            static (Message message, object? _) =>
            {
                var reader = message.GetBodyReader();
                return (Id: reader.ReadUInt32(), Key: reader.ReadString());
            },
            signal =>
            {
                if (signal.HasValue)
                    sink.OnActionInvoked(signal.Value.Id, signal.Value.Key);
            },
            ObserverFlags.None, emitOnCapturedContext: false).ConfigureAwait(false));

        // Spec 1.2+: emitted just before ActionInvoked, carrying the XDG activation token.
        _subscriptions.Add(await _connection.WatchSignalAsync(
            sender: null, ObjectPath, InterfaceName, "ActivationToken",
            static (Message message, object? _) =>
            {
                var reader = message.GetBodyReader();
                return (Id: reader.ReadUInt32(), Token: reader.ReadString());
            },
            signal =>
            {
                if (signal.HasValue)
                    sink.OnActivationToken(signal.Value.Id, signal.Value.Token);
            },
            ObserverFlags.None, emitOnCapturedContext: false).ConfigureAwait(false));
    }

    public ValueTask DisposeAsync()
    {
        foreach (var subscription in _subscriptions)
            subscription.Dispose();
        _subscriptions.Clear();
        _connection.Dispose();
        return ValueTask.CompletedTask;
    }
}
