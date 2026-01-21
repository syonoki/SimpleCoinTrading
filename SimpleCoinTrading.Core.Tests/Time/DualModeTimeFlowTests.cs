using System.Threading.Channels;
using SimpleCoinTrading.Core.Time;
using SimpleCoinTrading.Core.Time.Clocks;
using SimpleCoinTrading.Core.Time.TimeFlows;
using Xunit;

namespace SimpleCoinTrading.Core.Tests.Time;

public class DualModeTimeFlowTests
{
    private (DualModeTimeFlow flow, VirtualClock clock) CreateFlow(TimeFlowMode mode, TimeSpan? step = null, TimeSpan? poll = null)
    {
        var clock = new VirtualClock();
        var flow = new DualModeTimeFlow(clock, mode, step, poll);
        return (flow, clock);
    }

    [Fact]
    public async Task BacktestMode_ShouldEmitTicksImmediately()
    {
        // Arrange
        var step = TimeSpan.FromSeconds(10);
        var (flow, clock) = CreateFlow(TimeFlowMode.Backtest, step: step);
        using var cts = new CancellationTokenSource();
        flow.Start(cts.Token);

        var start = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        
        // Act
        flow.AdvanceTo(start); // First call sets the baseline
        flow.AdvanceTo(start.AddSeconds(25)); // Should emit ticks at 00:10, 00:20

        // Assert
        var reader = flow.Ticks;
        
        // Baseline tick
        Assert.True(reader.TryRead(out var t0));
        Assert.Equal(start, t0.UtcNow);
        Assert.Equal(start.AddSeconds(25), clock.UtcNow);

        // Ticks from AdvanceTo
        Assert.True(reader.TryRead(out var t1));
        Assert.Equal(start.AddSeconds(10), t1.UtcNow);

        Assert.True(reader.TryRead(out var t2));
        Assert.Equal(start.AddSeconds(20), t2.UtcNow);

        Assert.False(reader.TryRead(out _));
    }

    [Fact]
    public async Task RealTimeReplayMode_ShouldEmitTicksGradually()
    {
        // Arrange
        var step = TimeSpan.FromMilliseconds(50);
        var poll = TimeSpan.FromMilliseconds(10);
        var (flow, clock) = CreateFlow(TimeFlowMode.RealTimeReplay, step: step, poll: poll);
        using var cts = new CancellationTokenSource();
        flow.Start(cts.Token);

        var start = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        
        // Act
        flow.AdvanceTo(start);
        
        // Set cap to 200ms ahead
        var cap = start.AddMilliseconds(200);
        flow.AdvanceTo(cap);

        // Assert
        var reader = flow.Ticks;
        
        // Baseline
        Assert.True(await reader.WaitToReadAsync(cts.Token));
        Assert.True(reader.TryRead(out var t0));
        Assert.Equal(start, t0.UtcNow);

        // Wait for background loop to emit ticks
        // 50ms, 100ms, 150ms, 200ms
        var ticks = new List<DateTime>();
        var timeout = Task.Delay(2000); // 넉넉히 기다림
        while (ticks.Count < 4)
        {
            var readTask = reader.ReadAsync(cts.Token).AsTask();
            var completed = await Task.WhenAny(readTask, timeout);
            if (completed == timeout) break;
            ticks.Add((await readTask).UtcNow);
        }

        Assert.Equal(4, ticks.Count);
        Assert.Equal(start.AddMilliseconds(50), ticks[0]);
        Assert.Equal(start.AddMilliseconds(100), ticks[1]);
        Assert.Equal(start.AddMilliseconds(150), ticks[2]);
        Assert.Equal(start.AddMilliseconds(200), ticks[3]);
        
        Assert.Equal(cap, clock.UtcNow);
    }

    [Fact]
    public async Task RealTimeReplayMode_ShouldNotExceedCap()
    {
        // Arrange
        var step = TimeSpan.FromMilliseconds(10);
        var poll = TimeSpan.FromMilliseconds(10);
        var (flow, clock) = CreateFlow(TimeFlowMode.RealTimeReplay, step: step, poll: poll);
        using var cts = new CancellationTokenSource();
        flow.Start(cts.Token);

        var start = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        flow.AdvanceTo(start);

        // Act
        // Cap is only 20ms ahead
        flow.AdvanceTo(start.AddMilliseconds(20));

        // Wait enough time that it WOULD have gone further if not capped
        await Task.Delay(200);

        // Assert
        var reader = flow.Ticks;
        var count = 0;
        while (reader.TryRead(out var t))
        {
            count++;
            Assert.True(t.UtcNow <= start.AddMilliseconds(20));
        }
        
        // start (t0), 10ms, 20ms = 3 ticks
        Assert.Equal(3, count);
        Assert.Equal(start.AddMilliseconds(20), clock.UtcNow);
    }

    [Fact]
    public void SetMode_ShouldChangeBehavior()
    {
        // Arrange
        var (flow, _) = CreateFlow(TimeFlowMode.RealTimeReplay);
        var start = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        flow.AdvanceTo(start);

        // Act
        flow.SetMode(TimeFlowMode.Backtest);
        flow.AdvanceTo(start.AddSeconds(10));

        // Assert
        // In Backtest mode, it should have emitted ticks immediately even without Start() loop
        var reader = flow.Ticks;
        var ticks = new List<DateTime>();
        while (reader.TryRead(out var t)) ticks.Add(t.UtcNow);

        Assert.Contains(start.AddSeconds(10), ticks);
    }
}
