using Hark.Windows;

namespace Hark.Tests;

/// <summary>Records the toasts the Windows manager asks for and lets a test play their events.</summary>
internal sealed class FakeToastPlatform : IToastPlatform
{
    public List<ToastRequest> Shown { get; } = [];
    public List<string> Hidden { get; } = [];
    public IToastEventSink? Sink { get; private set; }

    public void Show(ToastRequest request) => Shown.Add(request);

    public void Hide(string tag) => Hidden.Add(tag);

    public void SetSink(IToastEventSink sink) => Sink = sink;
}
