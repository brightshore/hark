namespace Hark.Linux;

/// <summary>
/// The <c>org.freedesktop.Notifications</c> wire surface, and nothing else. Everything Hark decides
/// lives in <see cref="FreeDesktopNotificationManager"/>, which talks only to this, so the decisions
/// are testable without a bus. <see cref="DBusNotificationsBus"/> is the one real implementation.
/// </summary>
internal interface INotificationsBus : IAsyncDisposable
{
    Task<string[]> GetCapabilitiesAsync();

    Task<uint> NotifyAsync(NotifyRequest request);

    /// <summary>Throws <c>DBusErrorReplyException</c> when the id is unknown to the server.</summary>
    Task CloseNotificationAsync(uint id);

    /// <summary>Starts delivering the interface's three signals to <paramref name="sink"/>.</summary>
    Task SubscribeAsync(INotificationSignalSink sink);
}

/// <summary>Receives the <c>org.freedesktop.Notifications</c> signals, on a bus thread.</summary>
internal interface INotificationSignalSink
{
    void OnNotificationClosed(uint id, uint reason);

    void OnActionInvoked(uint id, string actionKey);

    void OnActivationToken(uint id, string activationToken);
}

/// <summary>The arguments of <c>Notify(susssasa{sv}i)</c>, in order.</summary>
internal sealed record NotifyRequest(
    string AppName,
    uint ReplacesId,
    string AppIcon,
    string Summary,
    string Body,
    string[] Actions,
    NotifyHints Hints,
    int ExpireTimeout);

/// <summary>The hints Hark sends. Null or default members are left out of the <c>a{sv}</c> entirely.</summary>
internal sealed record NotifyHints(byte Urgency, string? DesktopEntry, string? ImagePath, bool SuppressSound);
