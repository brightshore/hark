namespace Hark;

/// <summary>
/// Who the notifications come from. Every platform needs this, under a different name.
/// </summary>
/// <param name="AppId">
/// A stable reverse-DNS id such as <c>com.example.MyApp</c>. On Linux it is sent as the
/// <c>desktop-entry</c> hint, so it should match your <c>.desktop</c> file's basename; on Windows it
/// becomes the AppUserModelID; on macOS it is the bundle identifier.
/// </param>
/// <param name="DisplayName">The human-readable app name shown alongside the notification.</param>
public sealed record AppIdentity(string AppId, string DisplayName)
{
    /// <summary>
    /// Absolute path to the app icon, for platforms that take it per notification (Linux).
    /// Windows and macOS use the icon of the shortcut or bundle instead.
    /// </summary>
    public string? IconPath { get; init; }
}
