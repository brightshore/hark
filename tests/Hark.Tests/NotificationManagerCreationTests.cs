using AwesomeAssertions;
using Hark.Avalonia;

namespace Hark.Tests;

[TestClass]
public sealed class NotificationManagerCreationTests
{
    [TestMethod]
    public async Task A_backend_that_fails_unexpectedly_gives_an_unsupported_manager_that_says_why()
    {
        // "Never throws" has to hold for failures no backend foresaw: Hark.Avalonia awaits one cached
        // task for the whole run, and a faulted one would throw at every use.
        var manager = await NotificationManager.CreateGuardedAsync(
            () => throw new System.Net.Sockets.SocketException(111), CancellationToken.None);

        manager.IsSupported.Should().BeFalse();
        manager.UnsupportedReason.Should().Contain("SocketException");
    }

    [TestMethod]
    public async Task Cancellation_is_still_cancellation()
    {
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        var act = () => NotificationManager.CreateGuardedAsync(
            () => Task.FromCanceled<INotificationManager>(cts.Token), cts.Token);

        await act.Should().ThrowAsync<OperationCanceledException>();
    }

    [TestMethod]
    public async Task Disposing_the_dispatching_wrapper_lets_go_of_the_inner_manager()
    {
        var inner = new CountingManager();
        var wrapper = new DispatchingNotificationManager(inner);
        wrapper.Activated += (_, _) => { };
        wrapper.Closed += (_, _) => { };
        inner.Subscribers.Should().Be(2);

        await wrapper.DisposeAsync();

        inner.Subscribers.Should().Be(0, "a disposed wrapper must not stay reachable from the manager it wrapped");
    }

    private sealed class CountingManager : INotificationManager
    {
        public int Subscribers;
        public bool IsSupported => true;
        public string? UnsupportedReason => null;
        public NotificationCapabilities Capabilities => NotificationCapabilities.Show;
        public Task<NotificationHandle> ShowAsync(Notification notification, CancellationToken cancellationToken = default) =>
            Task.FromResult(new NotificationHandle("1", null));
        public Task CloseAsync(NotificationHandle handle, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public event EventHandler<NotificationActivatedEventArgs>? Activated { add => Subscribers++; remove => Subscribers--; }
        public event EventHandler<NotificationClosedEventArgs>? Closed { add => Subscribers++; remove => Subscribers--; }
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}
