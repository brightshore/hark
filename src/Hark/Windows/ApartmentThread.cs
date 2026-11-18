using System.Collections.Concurrent;

namespace Hark.Windows;

/// <summary>
/// One thread that runs the work it is given, in order. On Windows it is a single-threaded apartment,
/// which is where the shell's apartment objects belong: a TaskbarList made on it is called directly,
/// with no proxy between the caller's raw vtable calls and the object. Elsewhere it is an ordinary
/// thread, so the queue itself is testable on any OS.
/// </summary>
/// <remarks>
/// The thread starts on first use and ends after <see cref="Dispose"/>. It is a background thread, so
/// it never keeps a process alive. Work that waits on it never blocks the caller's thread.
/// </remarks>
internal sealed class ApartmentThread : IDisposable
{
    private readonly BlockingCollection<(Action Work, TaskCompletionSource Done)> _queue = [];
    private readonly Lazy<Thread> _thread;

    public ApartmentThread(string name)
    {
        _thread = new Lazy<Thread>(() =>
        {
            var thread = new Thread(Run) { IsBackground = true, Name = name };
            if (OperatingSystem.IsWindows())
                thread.SetApartmentState(ApartmentState.STA);
            thread.Start();
            return thread;
        });
    }

    /// <summary>Whether the calling code is running on this thread.</summary>
    public bool IsCurrent => _thread.IsValueCreated && Thread.CurrentThread == _thread.Value;

    /// <summary>Runs <paramref name="work"/> on the thread; the task ends with it, faulted if it threw.</summary>
    /// <exception cref="ObjectDisposedException">The thread has been disposed.</exception>
    public Task RunAsync(Action work)
    {
        var done = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        try
        {
            _queue.Add((work, done));
        }
        catch (InvalidOperationException)
        {
            // Added after CompleteAdding.
            throw new ObjectDisposedException(nameof(ApartmentThread));
        }
        _ = _thread.Value;
        return done.Task;
    }

    private void Run()
    {
        foreach (var (work, done) in _queue.GetConsumingEnumerable())
        {
            try
            {
                work();
                done.TrySetResult();
            }
            catch (Exception ex)
            {
                done.TrySetException(ex);
            }
        }
    }

    /// <summary>Lets the work already given finish, then ends the thread.</summary>
    public void Dispose() => _queue.CompleteAdding();
}
