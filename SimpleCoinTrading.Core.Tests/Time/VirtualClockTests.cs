using SimpleCoinTrading.Core.Time.Clocks;
using Xunit;

namespace SimpleCoinTrading.Core.Tests.Time;

public class VirtualClockTests
{
    [Fact]
    public void SetUtc_ShouldUpdateTime()
    {
        // Arrange
        var clock = new VirtualClock();
        var time = new DateTime(2026, 1, 1, 12, 0, 0, DateTimeKind.Utc);

        // Act
        clock.SetUtc(time);

        // Assert
        Assert.Equal(time, clock.UtcNow);
        Assert.Equal(DateTimeKind.Utc, clock.UtcNow.Kind);
    }

    [Fact]
    public void SetUtc_ShouldForceUtcKind()
    {
        // Arrange
        var clock = new VirtualClock();
        var time = new DateTime(2026, 1, 1, 12, 0, 0, DateTimeKind.Unspecified);

        // Act
        clock.SetUtc(time);

        // Assert
        Assert.Equal(DateTimeKind.Utc, clock.UtcNow.Kind);
        Assert.Equal(time.Ticks, clock.UtcNow.Ticks);
    }

    [Fact]
    public void AdvanceToUtc_ShouldOnlyMoveForward()
    {
        // Arrange
        var clock = new VirtualClock();
        var initial = new DateTime(2026, 1, 1, 12, 0, 0, DateTimeKind.Utc);
        clock.SetUtc(initial);

        var forward = initial.AddHours(1);

        // Act
        clock.AdvanceToUtc(forward);

        // Assert
        Assert.Equal(forward, clock.UtcNow);
    }

    [Fact]
    public void AdvanceToUtc_ShouldIgnoreBackward()
    {
        // Arrange
        var clock = new VirtualClock();
        var initial = new DateTime(2026, 1, 1, 12, 0, 0, DateTimeKind.Utc);
        clock.SetUtc(initial);

        var backward = initial.AddHours(-1);

        // Act
        clock.AdvanceToUtc(backward);

        // Assert
        Assert.Equal(initial, clock.UtcNow);
    }

    [Fact]
    public void AdvanceToUtc_ThreadSafetyTest()
    {
        // Arrange
        var clock = new VirtualClock();
        var baseTime = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        clock.SetUtc(baseTime);

        int threadCount = 10;
        int iterations = 1000;
        var tasks = new Task[threadCount];

        // Act
        for (int i = 0; i < threadCount; i++)
        {
            int threadId = i;
            tasks[i] = Task.Run(() =>
            {
                for (int j = 0; j < iterations; j++)
                {
                    // Each thread tries to advance to a random future time
                    var target = baseTime.AddMilliseconds(Random.Shared.Next(1, 1000000));
                    clock.AdvanceToUtc(target);
                }
            });
        }

        Task.WaitAll(tasks);

        // Assert
        // The clock should have advanced, but we can't be sure of the exact value due to randomness.
        // The main point is that it didn't crash and it's still UTC.
        Assert.True(clock.UtcNow >= baseTime);
        Assert.Equal(DateTimeKind.Utc, clock.UtcNow.Kind);
    }
}
