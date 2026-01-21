using SimpleCoinTrading.Core.Time;
using SimpleCoinTrading.Core.Time.Clocks;
using Xunit;

namespace SimpleCoinTrading.Core.Tests.Time;

public class ClockTests
{
    [Fact]
    public void SystemClock_ShouldReturnUtcNow()
    {
        var clock = new SystemClock();
        var now = DateTime.UtcNow;
        var clockNow = clock.UtcNow;
        
        // Allow 1 second difference
        Assert.True((clockNow - now).Duration() < TimeSpan.FromSeconds(1));
    }

    [Fact]
    public void SimulatedClock_ShouldReturnSetTime()
    {
        var clock = new SimulatedClock();
        var time = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        
        clock.SetUtc(time);
        Assert.Equal(time, clock.UtcNow);
    }

    [Fact]
    public void ManualClock_ShouldAdvanceTime()
    {
        var start = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        var clock = new ManualClock(start);
        
        Assert.Equal(start, clock.UtcNow);
        
        clock.Advance(TimeSpan.FromHours(1));
        Assert.Equal(start.AddHours(1), clock.UtcNow);
    }
}
