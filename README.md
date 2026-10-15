# Hark

Native OS notifications for .NET desktop apps, with first-class Avalonia support.

Avalonia has no notification API of its own. The existing libraries either ship a UWP toolkit,
compile Objective-C at build time, or still sit on macOS's deprecated `NSUserNotificationCenter`.
Hark talks to each platform's own service directly, with no native build step.

| Platform | Service | Status |
| --- | --- | --- |
| Linux / BSD | `org.freedesktop.Notifications` over D-Bus | **Working** |
| Windows 10+ | WinRT toast notifications via COM | Planned |
| macOS 11+ | `UNUserNotificationCenter` via the Objective-C runtime | Planned |
| Flatpak | `org.freedesktop.portal.Notification` | Planned |

Platforms without a backend get a manager whose `IsSupported` is `false` and whose
`UnsupportedReason` says why. Nothing fails silently.

## Packages

- **Hark**: the API and every backend. No UI framework dependency, AOT-compatible.
- **Hark.Avalonia**: `AppBuilder.UseNotifications(...)`, plus event delivery on Avalonia's UI thread.

## Avalonia

```csharp
public static AppBuilder BuildAvaloniaApp() =>
    AppBuilder.Configure<App>()
        .UsePlatformDetect()
        .UseNotifications(new AppIdentity("com.example.MyApp", "My App"));

// Later, anywhere:
var notifications = await AvaloniaNotifications.Manager;
notifications.Activated += (_, e) => mainWindow.Activate(); // already on the UI thread

await notifications.ShowAsync(new Notification
{
    Title = "Export finished",
    Body = "report.pdf is ready",
    Actions = [new("open", "Open"), new("reveal", "Show in folder")],
});
```

## Without Avalonia

```csharp
await using var notifications = await NotificationManager.CreateAsync(new AppIdentity("com.example.Tool", "Tool"));
if (notifications.IsSupported)
    await notifications.ShowAsync(new Notification { Title = "Done" });
```

Events are raised on a background thread here.

## Concepts

- **`AppIdentity.AppId`**: a reverse-DNS id. Linux sends it as the `desktop-entry` hint, so match it to
  your `.desktop` file name. Windows will use it as the AppUserModelID, and macOS as the bundle id.
- **Capabilities** are discovered at startup, because on Linux they depend on the running daemon.
  Check `Capabilities.HasFlag(NotificationCapabilities.Actions)` before relying on buttons.
- **`Tag`** replaces a notification still on screen instead of stacking a new one.
- **`Body` is plain text.** Hark escapes it for servers that parse markup.
- **`ActivationToken`** (Wayland) is what lets a compositor raise your window when the user clicks.

## Dependencies

The Linux backend uses `Tmds.DBus.Protocol`, pinned to the version Avalonia 12 itself references,
so an Avalonia app ends up with one copy and one set of expectations. Hark targets Avalonia 12;
Avalonia 11 pins an older, API-incompatible `Tmds.DBus.Protocol`.

## Building

```bash
dotnet build Hark.slnx
dotnet test --solution Hark.slnx
```

On Linux, run the tests inside `dbus-run-session` to include the real-bus round trip. Elsewhere it
reports as inconclusive.

## License

MIT
