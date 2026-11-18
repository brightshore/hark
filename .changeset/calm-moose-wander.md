---
type: feat
"Hark"
"Hark.Avalonia"
---

Put a count or a short label on the app's icon, and ask the user to look at a window. `BadgeManager.CreateAsync` gives an `IBadgeManager` beside notifications: the Dock tile's badge on macOS (any text, and it works from an unbundled process), a count drawn as the taskbar overlay on Windows along with the taskbar button's progress bar, and the Unity launcher entry's count and progress on Linux, which KDE Plasma, Dash to Dock and Plank show. `Capabilities` says which of count, text and progress the platform has. Hark.Avalonia adds `AppBuilder.UseBadge`, which puts the Windows badge on the main window, and `WindowAttention`, which bounces the Dock icon on macOS (once, or until the app is active), flashes the taskbar button on Windows, and marks the window as demanding attention on X11 and XWayland, with `Cancel` to withdraw it.