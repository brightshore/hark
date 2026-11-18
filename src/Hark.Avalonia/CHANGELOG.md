# Hark.Avalonia

## 0.2.0

### 🚀 Enhancements

- Put a count or a short label on the app's icon, and ask the user to look at a window. `BadgeManager.CreateAsync` gives an `IBadgeManager` beside notifications: the Dock tile's badge on macOS (any text, and it works from an unbundled process), a count drawn as the taskbar overlay on Windows along with the taskbar button's progress bar, and the Unity launcher entry's count and progress on Linux, which KDE Plasma, Dash to Dock and Plank show. `Capabilities` says which of count, text and progress the platform has. Hark.Avalonia adds `AppBuilder.UseBadge`, which puts the Windows badge on the main window, and `WindowAttention`, which bounces the Dock icon on macOS (once, or until the app is active), flashes the taskbar button on Windows, and marks the window as demanding attention on X11 and XWayland, with `Cancel` to withdraw it.

### 🌊 Dependencies

- Hark@0.2.0

## 0.1.1

### 🩹 Fixes

- A click on a macOS notification shown by an earlier run of your app now reaches the app. Clicking one in Notification Center relaunches the app, and `Activated` fires with the handle that run had, tag included, instead of the click being dropped. A click that arrives before you subscribe to `Activated` is held for your first handler, as on Windows. `NotificationManager.CreateAsync` no longer throws on an unexpected platform failure: you get a manager whose `IsSupported` is false, with the error as `UnsupportedReason`. With Hark.Avalonia, this means `AvaloniaNotifications.Manager` can't fail for the rest of the run. Disposing the Avalonia wrapper now also detaches it from the manager it wraps.

### 🌊 Dependencies

- Hark@0.1.1

## 0.1.0

### 🚀 Enhancements

- First release. Native OS notifications for .NET desktop apps, with no UWP toolkit and no native build step: Linux and BSD through the freedesktop notification service on D-Bus, macOS through UNUserNotificationCenter, and Windows 10 1809+ through toasts, including clicks from Notification Center after the app has exited. One API covers title, body, image, buttons, urgency, silence, expiration and tag-based replacement, and reports clicks and closes. Hark.Avalonia adds AppBuilder.UseNotifications and delivers those events on the UI thread.

### 🌊 Dependencies

- Hark@0.1.0
