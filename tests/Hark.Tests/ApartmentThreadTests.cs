using AwesomeAssertions;
using Hark.Windows;

namespace Hark.Tests;

/// <summary>
/// The thread the Windows badge makes every taskbar call on. Every wait is bounded: a queue that
/// stopped running would otherwise hang the run instead of failing it.
/// </summary>
[TestClass]
public sealed class ApartmentThreadTests
{
    private static readonly TimeSpan Wait = TimeSpan.FromSeconds(10);

    [TestMethod]
    public async Task Work_runs_in_order_on_one_thread_that_is_not_the_callers()
    {
        using var apartment = new ApartmentThread("test");
        var threads = new List<int>();
        var order = new List<int>();

        var tasks = Enumerable.Range(1, 5).Select(i => apartment.RunAsync(() =>
        {
            threads.Add(Environment.CurrentManagedThreadId);
            order.Add(i);
        })).ToArray();
        await Task.WhenAll(tasks).WaitAsync(Wait);

        order.Should().Equal(1, 2, 3, 4, 5);
        threads.Distinct().Should().ContainSingle().Which.Should().NotBe(Environment.CurrentManagedThreadId);
    }

    [TestMethod]
    public async Task A_throwing_call_faults_its_task_and_the_thread_carries_on()
    {
        using var apartment = new ApartmentThread("test");

        var failing = apartment.RunAsync(() => throw new InvalidOperationException("HRESULT 0x80004005"));
        var next = apartment.RunAsync(() => { });

        await failing.Invoking(t => t.WaitAsync(Wait)).Should().ThrowAsync<InvalidOperationException>().WithMessage("*0x80004005*");
        await next.WaitAsync(Wait);
    }

    [TestMethod]
    public void A_disposed_thread_takes_no_more_work()
    {
        var apartment = new ApartmentThread("test");
        apartment.Dispose();

        Action act = () => apartment.RunAsync(() => { });

        act.Should().Throw<ObjectDisposedException>();
    }

    [TestMethod]
    public async Task On_Windows_it_is_a_single_threaded_apartment()
    {
        if (!OperatingSystem.IsWindows())
            Assert.Inconclusive("Windows only: apartments are a COM idea.");

        using var apartment = new ApartmentThread("test");
        var state = ApartmentState.Unknown;
        await apartment.RunAsync(() => state = Thread.CurrentThread.GetApartmentState()).WaitAsync(Wait);

        state.Should().Be(ApartmentState.STA);
    }
}
