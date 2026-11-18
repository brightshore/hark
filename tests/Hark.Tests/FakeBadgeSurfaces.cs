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

/// <summary>Records every taskbar call the Windows badge manager makes, at a chosen overlay size, and the thread it came on.</summary>
internal sealed class FakeTaskbar(int overlaySize = 16) : ITaskbar
{
    public List<(nint Window, BadgeIcon? Icon, string? Description)> Overlays { get; } = [];
    public List<(nint Window, double? Progress)> Progress { get; } = [];
    public List<int> CallingThreads { get; } = [];

    public int OverlaySize(nint window) => overlaySize;

    public void SetOverlay(nint window, BadgeIcon? icon, string? description)
    {
        CallingThreads.Add(Environment.CurrentManagedThreadId);
        Overlays.Add((window, icon, description));
    }

    public void SetProgress(nint window, double? progress)
    {
        CallingThreads.Add(Environment.CurrentManagedThreadId);
        Progress.Add((window, progress));
    }
}

/// <summary>Records the Xlib calls <see cref="X11Attention"/> makes, in order, and can play an error during XSync.</summary>
internal sealed class FakeXlib : IXlib
{
    public static readonly IntPtr Display = 0x5000;
    public static readonly IntPtr PreviousHandler = 0x7777;

    public List<string> Calls { get; } = [];
    public Action<IntPtr>? DuringSync { get; set; }
    public bool SendThrows { get; set; }

    public IntPtr OpenDisplay()
    {
        Calls.Add("open");
        return Display;
    }

    public void CloseDisplay(IntPtr display) => Calls.Add("close");

    public nuint DefaultRootWindow(IntPtr display) => 1;

    public nuint InternAtom(IntPtr display, string name)
    {
        Calls.Add($"atom {name}");
        return name == "_NET_WM_STATE" ? 300u : 301u;
    }

    public void SendEvent(IntPtr display, nuint destination, X11Attention.XClientMessageEvent message)
    {
        Calls.Add($"send {(message.Data0 == X11Attention.NetWmStateAdd ? "add" : "remove")} {message.Data1}");
        if (SendThrows)
            throw new InvalidOperationException("send failed");
    }

    public void Sync(IntPtr display)
    {
        Calls.Add("sync");
        DuringSync?.Invoke(display);
    }

    public IntPtr SetErrorHandler(IntPtr handler)
    {
        Calls.Add(handler == X11ErrorGuard.Handler ? "handler guard" : $"handler {handler}");
        return handler == X11ErrorGuard.Handler ? PreviousHandler : X11ErrorGuard.Handler;
    }
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
