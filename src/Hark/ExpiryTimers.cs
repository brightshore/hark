namespace Hark;

/// <summary>
/// One timer per notification id, for backends whose platform has no expiry of its own. Showing
/// the same id again (a tag replacement) replaces its timer; closing cancels it.
/// </summary>
/// <remarks>
/// Each timer fires its callback only if it is still the id's current timer, so a timer that was
/// replaced or cancelled while its callback was already queued does nothing.
/// </remarks>
internal sealed class ExpiryTimers(TimeProvider time) : IDisposable
{
    private readonly Lock _gate = new();
    private readonly Dictionary<string, Entry> _timers = new(StringComparer.Ordinal);
    private bool _disposed;

    private sealed class Entry
    {
        public ITimer? Timer;
    }

    /// <summary>Calls <paramref name="onExpired"/> after <paramref name="after"/>, replacing any timer for <paramref name="id"/>.</summary>
    public void Schedule(string id, TimeSpan after, Action onExpired)
    {
        var entry = new Entry();
        Entry? replaced;
        lock (_gate)
        {
            if (_disposed)
                return;
            _timers.Remove(id, out replaced);
            _timers[id] = entry;
            // Created inside the lock, so the callback cannot run before Timer is set.
            entry.Timer = time.CreateTimer(_ =>
            {
                lock (_gate)
                {
                    if (!_timers.TryGetValue(id, out var current) || current != entry)
                        return;
                    _timers.Remove(id);
                }
                entry.Timer?.Dispose();
                onExpired();
            }, null, after, Timeout.InfiniteTimeSpan);
        }
        replaced?.Timer?.Dispose();
    }

    public void Cancel(string id)
    {
        Entry? removed;
        lock (_gate)
            _timers.Remove(id, out removed);
        removed?.Timer?.Dispose();
    }

    public void Dispose()
    {
        Entry[] all;
        lock (_gate)
        {
            _disposed = true;
            all = [.. _timers.Values];
            _timers.Clear();
        }
        foreach (var entry in all)
            entry.Timer?.Dispose();
    }
}
