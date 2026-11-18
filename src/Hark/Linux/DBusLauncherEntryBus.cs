using System.Globalization;
using Tmds.DBus.Protocol;

namespace Hark.Linux;

/// <summary>
/// <see cref="ILauncherEntryBus"/> over a real session bus connection, which this owns.
/// </summary>
/// <remarks>
/// The protocol is one broadcast signal: docks that honour it listen for <c>Update</c> from anyone and
/// match the app by the URI in its body, so nothing has to be registered or owned first.
/// </remarks>
internal sealed class DBusLauncherEntryBus : ILauncherEntryBus
{
    internal const string InterfaceName = "com.canonical.Unity.LauncherEntry";
    internal const string ObjectPathPrefix = "/com/canonical/unity/launcherentry/";

    private readonly DBusConnection _connection;

    private DBusLauncherEntryBus(DBusConnection connection) => _connection = connection;

    public static async Task<DBusLauncherEntryBus> ConnectAsync(string address, CancellationToken cancellationToken)
    {
        var connection = new DBusConnection(address);
        try
        {
            await connection.ConnectAsync().AsTask().WaitAsync(cancellationToken).ConfigureAwait(false);
            return new DBusLauncherEntryBus(connection);
        }
        catch
        {
            connection.Dispose();
            throw;
        }
    }

    /// <summary>
    /// The path the signal is sent from. Docks ignore it, but libunity gives each app its own, so this
    /// does too: a stable number derived from the URI, since a path element allows only [A-Za-z0-9_].
    /// </summary>
    internal static string ObjectPath(string appUri)
    {
        var hash = 2166136261u; // FNV-1a
        foreach (var c in appUri)
            hash = (hash ^ c) * 16777619u;
        return ObjectPathPrefix + hash.ToString(CultureInfo.InvariantCulture);
    }

    public void Update(string appUri, LauncherEntryUpdate update)
    {
        var writer = _connection.GetMessageWriter();
        MessageBuffer message;
        try
        {
            writer.WriteSignalHeader(null, ObjectPath(appUri), InterfaceName, "Update", "sa{sv}");
            writer.WriteString(appUri);
            var properties = writer.WriteDictionaryStart();
            if (update.Count is { } count)
            {
                writer.WriteDictionaryEntryStart();
                writer.WriteString("count");
                writer.WriteVariant(VariantValue.Int64(count));
            }
            if (update.CountVisible is { } countVisible)
            {
                writer.WriteDictionaryEntryStart();
                writer.WriteString("count-visible");
                writer.WriteVariantBool(countVisible);
            }
            if (update.Progress is { } progress)
            {
                writer.WriteDictionaryEntryStart();
                writer.WriteString("progress");
                writer.WriteVariantDouble(progress);
            }
            if (update.ProgressVisible is { } progressVisible)
            {
                writer.WriteDictionaryEntryStart();
                writer.WriteString("progress-visible");
                writer.WriteVariantBool(progressVisible);
            }
            writer.WriteDictionaryEnd(properties);
            message = writer.CreateMessage();
        }
        finally
        {
            writer.Dispose();
        }

        if (!_connection.TrySendMessage(message))
            throw new InvalidOperationException("The session bus connection has closed, so the launcher badge could not be sent.");
    }

    public ValueTask DisposeAsync()
    {
        _connection.Dispose();
        return ValueTask.CompletedTask;
    }
}
