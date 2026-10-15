using Hark.Linux;

namespace Hark.Tests;

/// <summary>Records what the manager sends and lets a test play the server's signals.</summary>
internal sealed class FakeNotificationsBus(params string[] capabilities) : INotificationsBus
{
    private uint _nextId = 100;

    public List<NotifyRequest> Sent { get; } = [];
    public List<uint> CloseCalls { get; } = [];
    public Exception? CloseFailure { get; set; }
    public INotificationSignalSink? Sink { get; private set; }
    public bool Disposed { get; private set; }

    public Task<string[]> GetCapabilitiesAsync() => Task.FromResult(capabilities);

    public Task<uint> NotifyAsync(NotifyRequest request)
    {
        Sent.Add(request);
        // Like a real server: a replacement keeps its id, anything else gets a fresh one.
        return Task.FromResult(request.ReplacesId != 0 ? request.ReplacesId : _nextId++);
    }

    public Task CloseNotificationAsync(uint id)
    {
        CloseCalls.Add(id);
        return CloseFailure is null ? Task.CompletedTask : Task.FromException(CloseFailure);
    }

    public Task SubscribeAsync(INotificationSignalSink sink)
    {
        Sink = sink;
        return Task.CompletedTask;
    }

    public ValueTask DisposeAsync()
    {
        Disposed = true;
        return ValueTask.CompletedTask;
    }
}
