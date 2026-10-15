using AwesomeAssertions;
using Microsoft.Extensions.Time.Testing;

namespace Hark.Tests;

[TestClass]
public sealed class ExpiryTimersTests
{
    [TestMethod]
    public void Fires_once_when_the_time_is_up_and_not_before()
    {
        var time = new FakeTimeProvider();
        using var timers = new ExpiryTimers(time);
        var fired = 0;

        timers.Schedule("a", TimeSpan.FromSeconds(10), () => fired++);
        time.Advance(TimeSpan.FromSeconds(9.9));
        fired.Should().Be(0);
        time.Advance(TimeSpan.FromSeconds(0.1));
        time.Advance(TimeSpan.FromMinutes(1));

        fired.Should().Be(1);
    }

    [TestMethod]
    public void Rescheduling_an_id_replaces_its_timer()
    {
        var time = new FakeTimeProvider();
        using var timers = new ExpiryTimers(time);
        var fired = new List<string>();

        timers.Schedule("a", TimeSpan.FromSeconds(5), () => fired.Add("first"));
        time.Advance(TimeSpan.FromSeconds(4));
        timers.Schedule("a", TimeSpan.FromSeconds(5), () => fired.Add("second"));
        time.Advance(TimeSpan.FromSeconds(4));
        fired.Should().BeEmpty("the first timer was replaced before it fired");
        time.Advance(TimeSpan.FromSeconds(1));

        fired.Should().Equal("second");
    }

    [TestMethod]
    public void Cancel_and_dispose_stop_timers()
    {
        var time = new FakeTimeProvider();
        var timers = new ExpiryTimers(time);
        var fired = 0;

        timers.Schedule("a", TimeSpan.FromSeconds(1), () => fired++);
        timers.Schedule("b", TimeSpan.FromSeconds(1), () => fired++);
        timers.Cancel("a");
        timers.Dispose();
        timers.Schedule("c", TimeSpan.FromSeconds(1), () => fired++);
        time.Advance(TimeSpan.FromSeconds(5));

        fired.Should().Be(0);
    }
}
