using Hark.Avalonia;
using Hark.Avalonia.Attention;
using Hark.Linux;
using Hark.MacOS;
using Hark.Windows;

namespace Hark.Tests;

/// <summary>Records every label the macOS badge manager sets.</summary>
internal sealed class FakeDockTile : IDockTile
{
    public List<string?> Labels { get; } = [];

    public void SetBadgeLabel(string? label) => Labels.Add(label);
}

/// <summary>Records every LauncherEntry update the Linux badge manager sends.</summary>
internal sealed class FakeLauncherEntryBus : ILauncherEntryBus
{
    public List<(string AppUri, LauncherEntryUpdate Update)> Updates { get; } = [];
    public bool Disposed { get; private set; }

    public void Update(string appUri, LauncherEntryUpdate update) => Updates.Add((appUri, update));

    public ValueTask DisposeAsync()
    {
        Disposed = true;
        return ValueTask.CompletedTask;
    }
}

/// <summary>Records every taskbar call the Windows badge manager makes, at a chosen overlay size.</summary>
internal sealed class FakeTaskbar(int overlaySize = 16) : ITaskbar
{
    public List<(nint Window, BadgeIcon? Icon, string? Description)> Overlays { get; } = [];
    public List<(nint Window, double? Progress)> Progress { get; } = [];

    public int OverlaySize(nint window) => overlaySize;

    public void SetOverlay(nint window, BadgeIcon? icon, string? description) => Overlays.Add((window, icon, description));

    public void SetProgress(nint window, double? progress) => Progress.Add((window, progress));
}

/// <summary>Records the requests a <see cref="WindowAttention"/> passes on.</summary>
internal sealed class FakeAttentionPlatform : IAttentionPlatform
{
    public List<AttentionLevel> Requests { get; } = [];
    public int Cancels { get; private set; }

    public AttentionCapabilities Capabilities => AttentionCapabilities.Request | AttentionCapabilities.Cancel;

    public void Request(AttentionLevel level) => Requests.Add(level);

    public void Cancel() => Cancels++;
}
