# Hark

Native OS notifications for .NET desktop apps, with first-class Avalonia support. Also a badge on
the app's icon, and a way to ask the user to look at a window.

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

- **Hark**: the API and every backend, the app badge included. No UI framework dependency, AOT-compatible.
- **Hark.Avalonia**: `AppBuilder.UseNotifications(...)` and `UseBadge(...)`, event delivery on Avalonia's
  UI thread, and `WindowAttention`.

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
- **`Expiration`** removes a notification after a time and closes it as `Expired`. On Linux the daemon
  decides (GNOME ignores it); Windows and macOS always honour it.
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

`Expiration` removes the notification when the time is up and closes it as `Expired`, while the
app runs. Not available on macOS: `ActivationToken`.
`Urgency.Critical` maps to time-sensitive, which needs the time-sensitive entitlement to break
through Focus.

## Windows

Toasts go through WinRT called directly over COM: no UWP toolkit, no CsWinRT projection, and no
Windows-only target framework, so a plain `net10.0` app gets the backend. An unpackaged app has no
package identity, so on first use Hark sets the process's AppUserModelID to `AppIdentity.AppId` and
registers it for the current user under `HKCU\Software\Classes\AppUserModelId`, with
`DisplayName` and `IconPath`. No shortcut and no admin rights are needed.

Clicks are delivered through a COM activator Hark registers for the app (per user, also in HKCU),
so a click from Notification Center after the app has exited starts it again: Windows runs the app
with `-Embedding` and the click arrives as soon as the app creates its manager. The `Activated`
event holds such a click until the app subscribes, and gives it the handle the earlier run had.
`Expiration` is also given to Windows as the toast's own expiry, so it goes even after the app
exits; while the app runs it closes as `Expired`. Not available on Windows: `ActivationToken`. `Urgency.Low` skips the popup and goes straight to Notification
Center; `Critical` uses the `urgent` scenario (Windows 11).

## App badge

A count or a short label on the app's icon. It belongs to the app, not to a notification: it stays
until the app changes or clears it.

| Platform | Where it shows | Count | Text | Progress |
| --- | --- | --- | --- | --- |
| macOS | The Dock tile's badge | Yes | Yes | No |
| Windows | An overlay icon on the taskbar button, and its progress bar | Yes | No | Yes |
| Linux / BSD | The launcher entry (`com.canonical.Unity.LauncherEntry`) | Yes | No | Yes |

```csharp
// Avalonia: .UseBadge(identity) next to .UseNotifications(identity), then
var badge = await AvaloniaBadge.Manager;
// Elsewhere: await BadgeManager.CreateAsync(identity)

if (badge.Capabilities.HasFlag(BadgeCapabilities.Count))
    await badge.SetCountAsync(unread);     // 0 clears it
if (badge.Capabilities.HasFlag(BadgeCapabilities.Progress))
    await badge.SetProgressAsync(0.4);     // 0 to 1; null hides it
await badge.ClearAsync();
```

Asking for something the platform's badge cannot show throws `NotSupportedException`, so check
`Capabilities` first. Where there is no badge at all, `IsSupported` is false and `UnsupportedReason`
says why.

- **macOS**: `NSApp.dockTile.badgeLabel`, any short text. Unlike notifications, it **works from an
  unbundled process** (`dotnet run` included): all it needs is the app's `NSApplication`, so create
  the badge once the UI framework has started (`UseBadge` does). Every change, from any thread, is
  queued on the main run loop, so changes apply in the order they were made: the returned task
  completes when the change is queued, not when the Dock shows it.
- **Windows**: `ITaskbarList3::SetOverlayIcon`, over raw COM like the toasts. An overlay is 16 pixels
  at 100% scaling, so Windows gets a count and no text: Hark draws it, white on a red disc, at the
  window's scaling, and anything over 99 reads "99+". It goes on a window's taskbar button: with
  `UseBadge`, the main window; otherwise `BadgeOptions.WindowHandle`, or else the process's first
  visible window. Before there is a window, setting a badge throws and says so. Progress is
  `SetProgressValue`. Call it from any thread: the taskbar calls are made on a single-threaded
  apartment thread the badge owns, which is where the shell's TaskbarList belongs.
- **Linux / BSD**: the `Update` signal of Unity's LauncherEntry protocol on the session bus, for
  `application://<AppId>.desktop`, so `AppIdentity.AppId` must match your installed `.desktop` file.
  **KDE Plasma's task manager, Dash to Dock and Plank show it; GNOME's own dash does not.** The
  signal has no reply, so a desktop that ignores it cannot be detected.

## Attention

`WindowAttention` (Hark.Avalonia) asks the user to look at a window that is not in front. It takes a
`TopLevel`, because Windows and X11 work per window, and is used on the UI thread.

```csharp
var attention = new WindowAttention(mainWindow);
if (attention.IsSupported)
    attention.Request(AttentionLevel.Critical); // or Informational, the default
// Later, if it no longer matters:
attention.Cancel();
```

| Platform | What happens | Informational | Critical | Cancel |
| --- | --- | --- | --- | --- |
| macOS | `[NSApp requestUserAttention:]`: the Dock icon bounces | Once | Until the app is active | Yes |
| Windows | `FlashWindowEx` on the taskbar button | Flashes once, stays lit | Flashes until the window is in front | Yes |
| X11 | `_NET_WM_STATE_DEMANDS_ATTENTION` | The window manager decides | Same as informational | Yes |

Every platform ignores a request for the window, or app, already in front, and ends it when the
user comes. On X11 the request uses a connection of its own, and an error the X server returns for
it (say, for a window already destroyed) becomes an `InvalidOperationException` rather than Xlib's
default of ending the process. Avalonia 12 has no Wayland backend of its own: on a Wayland desktop its windows run under
XWayland and get the X11 request, which the compositor shows its own way. A window with any other
kind of handle reports unsupported, with the reason.

## Dependencies

The Linux backend uses `Tmds.DBus.Protocol`, pinned to the version Avalonia 12 itself references,
so an Avalonia app ends up with one copy and one set of expectations. Hark targets Avalonia 12;
Avalonia 11 pins an older, API-incompatible `Tmds.DBus.Protocol`.

## Sample app

`demos/Hark.Sample` is a small Avalonia app for trying a backend by hand: every `Notification`
property has a control, the badge and the attention request have theirs, and every call and event
goes into a log. The attention request waits 3 seconds, to give you time to switch to another app.

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
