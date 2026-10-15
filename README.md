# Hark

Native OS notifications for .NET desktop apps, with first-class Avalonia support.

Avalonia has no notification API of its own. The existing libraries either ship a UWP toolkit,
compile Objective-C at build time, or still sit on macOS's deprecated `NSUserNotificationCenter`.
Hark talks to each platform's own service directly, with no native build step: D-Bus on Linux,
and the Objective-C runtime called from C# on macOS.

| Platform | Service | Status |
| --- | --- | --- |
| Linux / BSD | `org.freedesktop.Notifications` over D-Bus | **Working** |
| Windows 10 1809+ | WinRT toasts through raw COM | **Working** |
| macOS 11+ | `UNUserNotificationCenter` via the Objective-C runtime | **Working** |
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

## macOS

macOS only delivers notifications to an app running from a **signed `.app` bundle** with a
`CFBundleIdentifier`. `dotnet run` and `dotnet MyApp.dll` are not bundles, so there the manager
reports unsupported (with that reason) rather than crashing. Ad-hoc signing (`codesign --sign -`)
is enough on the machine that built it. `AppIdentity.AppId` is not used on macOS: the bundle's
identifier is.

The first `CreateAsync` shows the system permission prompt. macOS remembers a refusal per bundle
id and does not prompt again, so a refused app has to be switched on in System Settings >
Notifications; `UnsupportedReason` says so.

Not available on macOS: `Expiration` (no per-notification timeout) and `ActivationToken`.
`Urgency.Critical` maps to time-sensitive, which needs the time-sensitive entitlement to break
through Focus.

## Windows

Toasts go through WinRT called directly over COM: no UWP toolkit, no CsWinRT projection, and no
Windows-only target framework, so a plain `net10.0` app gets the backend. An unpackaged app has no
package identity, so on first use Hark sets the process's AppUserModelID to `AppIdentity.AppId` and
registers it for the current user under `HKCU\Software\Classes\AppUserModelId`, with
`DisplayName` and `IconPath`. No shortcut and no admin rights are needed.

Clicks are delivered while the app is running, including from Notification Center. A click after
the app has exited needs a COM activator, which is not registered yet. Not available on Windows:
`Expiration` and `ActivationToken`. `Urgency.Low` skips the popup and goes straight to Notification
Center; `Critical` uses the `urgent` scenario (Windows 11).

## Dependencies

The Linux backend uses `Tmds.DBus.Protocol`, pinned to the version Avalonia 12 itself references,
so an Avalonia app ends up with one copy and one set of expectations. Hark targets Avalonia 12;
Avalonia 11 pins an older, API-incompatible `Tmds.DBus.Protocol`.

## Sample app

`demos/Hark.Sample` is a small Avalonia app for trying a backend by hand: every `Notification`
property has a control, and every call and event goes into a log.

```bash
dotnet run --project demos/Hark.Sample
```

On macOS the build also lays the output out as a signed `Hark Sample.app` next to the binaries,
because macOS will not notify an unbundled process:

```bash
open "demos/Hark.Sample/bin/Debug/net10.0/Hark Sample.app"
```

On a platform without a backend it opens and says why it cannot show notifications.

## Building

```bash
dotnet build Hark.slnx
dotnet test --solution Hark.slnx
```

On Linux, run the tests inside `dbus-run-session` to include the real-bus round trip. Elsewhere it
reports as inconclusive.

## License

MIT
