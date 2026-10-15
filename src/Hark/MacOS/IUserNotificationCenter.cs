namespace Hark.MacOS;

/// <summary>
/// The <c>UNUserNotificationCenter</c> surface Hark uses, and nothing else. Every decision lives
/// in <see cref="MacNotificationManager"/>, which talks only to this, so it is testable on any OS.
/// <see cref="UserNotificationCenter"/> is the one real implementation.
/// </summary>
internal interface IUserNotificationCenter
{
    /// <summary>
    /// Asks for alert and sound permission; the first call shows the system prompt. Error is the
    /// OS's own explanation when it refused outright rather than the user saying no.
    /// </summary>
    Task<(bool Granted, string? Error)> RequestAuthorizationAsync();

    /// <summary>Replaces the full set of registered categories (the OS has no "add one").</summary>
    void SetCategories(IReadOnlyCollection<MacCategory> categories);

    /// <summary>Delivers, or replaces the delivered request with the same identifier.</summary>
    Task AddAsync(MacNotificationRequest request);

    /// <summary>Removes a delivered or pending request. Unknown identifiers are ignored by the OS.</summary>
    void Remove(string identifier);

    /// <summary>Where the user's responses go, on an OS thread.</summary>
    void SetResponseSink(IMacResponseSink sink);
}

/// <summary>Receives what the user did with a notification.</summary>
internal interface IMacResponseSink
{
    void OnResponse(string requestIdentifier, string actionIdentifier);
}

/// <summary>A category: the set of buttons a notification carries. Always asks for dismiss events.</summary>
internal sealed record MacCategory(string Identifier, IReadOnlyList<NotificationAction> Actions);

/// <summary>What becomes a <c>UNNotificationRequest</c>.</summary>
internal sealed record MacNotificationRequest(
    string Identifier,
    string Title,
    string Body,
    string CategoryIdentifier,
    string? ImagePath,
    bool PlaySound,
    MacInterruptionLevel InterruptionLevel);

/// <summary><c>UNNotificationInterruptionLevel</c>, macOS 12+.</summary>
internal enum MacInterruptionLevel : ulong
{
    Passive = 0,
    Active = 1,
    TimeSensitive = 2,
}
