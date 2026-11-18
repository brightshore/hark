using Tmds.DBus.Protocol;

namespace Hark.Linux;

/// <summary>
/// The launcher badge through Unity's <c>com.canonical.Unity.LauncherEntry</c> protocol, which KDE
/// Plasma's task manager, Dash to Dock and Plank honour. GNOME's own dash does not.
/// </summary>
/// <remarks>
/// The dock finds the app by <c>application://&lt;AppId&gt;.desktop</c>, so <see cref="AppIdentity.AppId"/>
/// must match the app's installed <c>.desktop</c> file name. Nothing answers the signal, so whether a
/// dock shows it cannot be known from here: the manager is supported wherever there is a session bus.
/// </remarks>
internal sealed class LauncherEntryBadgeManager : IBadgeManager
{
    private const BadgeCapabilities LauncherCapabilities = BadgeCapabilities.Count | BadgeCapabilities.Progress;

    private readonly ILauncherEntryBus _bus;
    private bool _disposed;

    private LauncherEntryBadgeManager(AppIdentity identity, ILauncherEntryBus bus)
    {
        AppUri = $"application://{identity.AppId}.desktop";
        _bus = bus;
    }

    public bool IsSupported => true;
    public string? UnsupportedReason => null;
    public BadgeCapabilities Capabilities => LauncherCapabilities;

    /// <summary>How the dock knows which launcher the badge is for.</summary>
    internal string AppUri { get; }

    /// <summary>Connects to the real session bus. Unavailability becomes an unsupported manager, not an exception.</summary>
    public static async Task<IBadgeManager> CreateAsync(AppIdentity identity, CancellationToken cancellationToken)
    {
        var address = DBusAddress.Session;
        if (string.IsNullOrEmpty(address))
            return new UnsupportedBadgeManager("No D-Bus session bus: DBUS_SESSION_BUS_ADDRESS is not set.");

        try
        {
            return Create(identity, await DBusLauncherEntryBus.ConnectAsync(address, cancellationToken).ConfigureAwait(false));
        }
        catch (DBusExceptionBase ex)
        {
            return new UnsupportedBadgeManager($"The session bus could not be reached: {ex.Message}");
        }
    }

    /// <summary>The seam the tests use: any <see cref="ILauncherEntryBus"/>, which the manager then owns.</summary>
    internal static LauncherEntryBadgeManager Create(AppIdentity identity, ILauncherEntryBus bus) => new(identity, bus);

    public Task SetCountAsync(int count, CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        BadgeValidation.ValidateCount(count);
        cancellationToken.ThrowIfCancellationRequested();

        _bus.Update(AppUri, new LauncherEntryUpdate(Count: count, CountVisible: count > 0));
        return Task.CompletedTask;
    }

    public Task SetTextAsync(string? text, CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        BadgeValidation.RequireCapability(LauncherCapabilities, BadgeCapabilities.Text);
        return Task.CompletedTask;
    }

    public Task SetProgressAsync(double? progress, CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        BadgeValidation.ValidateProgress(progress);
        cancellationToken.ThrowIfCancellationRequested();

        _bus.Update(AppUri, progress is { } value
            ? new LauncherEntryUpdate(Progress: value, ProgressVisible: true)
            : new LauncherEntryUpdate(ProgressVisible: false));
        return Task.CompletedTask;
    }

    public Task ClearAsync(CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        _bus.Update(AppUri, new LauncherEntryUpdate(Count: 0, CountVisible: false, ProgressVisible: false));
        return Task.CompletedTask;
    }

    public async ValueTask DisposeAsync()
    {
        if (_disposed)
            return;
        _disposed = true;
        await _bus.DisposeAsync().ConfigureAwait(false);
    }
}
