using SimpleCoinTrading.Core.Time;
using SimpleCoinTrading.Core.Time.Clocks;
using SimpleCoinTrading.Core.Time.TimeFlows;
using Xunit;

namespace SimpleCoinTrading.Core.Tests.Time;

public class LiveTimeFlowTests
{
    [Fact]
    public async Task Start_ShouldProduceTicks()
    {
        // Arrange
        var clock = new ManualClock(new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc));
        var period = TimeSpan.FromMilliseconds(10);
        var flow = new LiveTimeFlow(clock, period);
        using var cts = new CancellationTokenSource();

        // Act
        flow.Start(cts.Token);

        // Assert
        var reader = flow.Ticks;
        
        // 첫 번째 틱 수신 시도
        var tick1 = await reader.ReadAsync(cts.Token);
        Assert.Equal(clock.UtcNow, tick1.UtcNow);

        // 시간 진행 후 두 번째 틱 확인
        clock.Advance(period);
        var tick2 = await reader.ReadAsync(cts.Token);
        Assert.Equal(clock.UtcNow, tick2.UtcNow);

        cts.Cancel();
    }

    [Fact]
    public async Task Start_ShouldStopWhenCanceled()
    {
        // Arrange
        var clock = new SystemClock();
        var period = TimeSpan.FromMilliseconds(10);
        var flow = new LiveTimeFlow(clock, period);
        using var cts = new CancellationTokenSource();

        // Act
        flow.Start(cts.Token);
        
        // 한 번은 읽음
        await flow.Ticks.ReadAsync(cts.Token);
        
        // 취소
        cts.Cancel();

        // Assert
        // Completion task가 완료되어야 함
        await flow.Ticks.Completion;
        Assert.True(true); // 도달하면 성공
    }
}
