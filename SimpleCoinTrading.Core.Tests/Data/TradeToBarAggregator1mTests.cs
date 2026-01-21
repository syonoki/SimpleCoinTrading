using SimpleCoinTrading;
using SimpleCoinTrading.Core.Data;
using SimpleCoinTrading.Core.Time;
using SimpleCoinTrading.Core.Time.Clocks;
using SimpleCoinTrading.Core.Time.TimeFlows;
using Xunit;

namespace SimpleCoinTrading.Core.Tests.Data;

public class TradeToBarAggregator1mTests
{
    private class NoOpTimeAdvancer : ITimeAdvancer
    {
        public void AdvanceTo(DateTime marketUtc) { }
    }

    [Fact]
    public void OnTrade_ShouldAggregateSingleTick()
    {
        // Arrange
        BarClosedEvent? capturedEvent = null;
        var aggregator = new TradeToBarAggregator1m(e => capturedEvent = e);
        var time = new DateTime(2026, 1, 15, 10, 0, 5, DateTimeKind.Utc);
        var tick = new TradeTick(time, 50000m, 1.5m, true);

        // Act
        aggregator.OnTrade("BTC", tick);
        
        // 아직 분이 바뀌지 않았으므로 publish되지 않아야 함
        Assert.Null(capturedEvent);

        // Act: 다음 분의 틱을 넣어 이전 분을 publish 시킴
        var nextTime = time.AddMinutes(1);
        aggregator.OnTrade("BTC", new TradeTick(nextTime, 51000m, 1.0m, true));

        // Assert
        Assert.NotNull(capturedEvent);
        Assert.Equal("BTC", capturedEvent.Value.Symbol);
        Assert.Equal(Resolution.M1, capturedEvent.Value.Resolution);
        Assert.Equal(new DateTime(2026, 1, 15, 10, 0, 0, DateTimeKind.Utc), capturedEvent.Value.BarTimeUtc);
        
        var bar = capturedEvent.Value.Bar;
        Assert.Equal(50000m, bar.Open);
        Assert.Equal(50000m, bar.High);
        Assert.Equal(50000m, bar.Low);
        Assert.Equal(50000m, bar.Close);
        Assert.Equal(1.5m, bar.Volume);
    }

    [Fact]
    public void OnTrade_ShouldAggregateMultipleTicksInSameMinute()
    {
        // Arrange
        BarClosedEvent? capturedEvent = null;
        var aggregator = new TradeToBarAggregator1m(e => capturedEvent = e);
        var baseTime = new DateTime(2026, 1, 15, 10, 0, 0, DateTimeKind.Utc);

        // Act
        aggregator.OnTrade("BTC", new TradeTick(baseTime.AddSeconds(10), 100m, 1m, true));
        aggregator.OnTrade("BTC", new TradeTick(baseTime.AddSeconds(20), 120m, 2m, false));
        aggregator.OnTrade("BTC", new TradeTick(baseTime.AddSeconds(30), 80m, 3m, true));
        aggregator.OnTrade("BTC", new TradeTick(baseTime.AddSeconds(40), 110m, 4m, false));

        // 분 변경을 통한 발행
        aggregator.OnTrade("BTC", new TradeTick(baseTime.AddMinutes(1), 100m, 1m, true));

        // Assert
        Assert.NotNull(capturedEvent);
        var bar = capturedEvent.Value.Bar;
        Assert.Equal(100m, bar.Open);
        Assert.Equal(120m, bar.High);
        Assert.Equal(80m, bar.Low);
        Assert.Equal(110m, bar.Close);
        Assert.Equal(10m, bar.Volume);
    }

    [Fact]
    public void OnTrade_ShouldIgnoreOutOfOrderTicks()
    {
        // Arrange
        BarClosedEvent? capturedEvent = null;
        var aggregator = new TradeToBarAggregator1m(e => capturedEvent = e);
        var baseTime = new DateTime(2026, 1, 15, 10, 0, 0, DateTimeKind.Utc);

        // Act
        // 1. 첫 번째 틱 (10:00:30)
        aggregator.OnTrade("BTC", new TradeTick(baseTime.AddSeconds(30), 100m, 1m, true));
        
        // 2. 과거 틱 (10:00:10) - bucket은 같지만 BucketTimeUtc 보다는 과거가 아님 (bucket == b.BucketTimeUtc)
        // 현재 구현은 bucket < b.BucketTimeUtc 인 경우만 무시함.
        // 같은 분 내에서 시간이 거꾸로 가는 경우는 현재 로직상 Add(tick)이 호출됨.
        aggregator.OnTrade("BTC", new TradeTick(baseTime.AddSeconds(10), 200m, 10m, true));

        // 3. 아주 먼 과거 틱 (09:59:59) - bucket < b.BucketTimeUtc 가 되어 무시되어야 함
        aggregator.OnTrade("BTC", new TradeTick(baseTime.AddSeconds(-1), 500m, 100m, true));

        // 다음 분 틱으로 발행 유도
        aggregator.OnTrade("BTC", new TradeTick(baseTime.AddMinutes(1), 100m, 1m, true));

        // Assert
        Assert.NotNull(capturedEvent);
        // 첫 번째와 두 번째 틱은 합산됨 (1m + 10m = 11m)
        // 세 번째 틱(500m, 100m)은 무시되었어야 함.
        Assert.Equal(11m, capturedEvent.Value.Bar.Volume);
        Assert.DoesNotContain(capturedEvent.Value.Bar.Volume, new[] { 111m });
    }

    [Fact]
    public void FlushIfMinutePassed_ShouldPublishWhenTimePassed()
    {
        // Arrange
        BarClosedEvent? capturedEvent = null;
        var aggregator = new TradeToBarAggregator1m(e => capturedEvent = e);
        var baseTime = new DateTime(2026, 1, 15, 10, 0, 0, DateTimeKind.Utc);

        aggregator.OnTrade("BTC", new TradeTick(baseTime.AddSeconds(30), 100m, 1m, true));
        Assert.Null(capturedEvent);

        // Act: 아직 같은 분이면 flush 안 됨
        aggregator.FlushIfMinutePassed("BTC", baseTime.AddSeconds(59));
        Assert.Null(capturedEvent);

        // Act: 분이 지나면 flush 됨
        aggregator.FlushIfMinutePassed("BTC", baseTime.AddMinutes(1));
        
        // Assert
        Assert.NotNull(capturedEvent);
        Assert.Equal(baseTime, capturedEvent.Value.BarTimeUtc);
    }

    [Fact]
    public void MarketPipeline_ShouldFlushBarWhenOrderBookComes()
    {
        // Arrange
        BarClosedEvent? capturedBar = null;
        var clock = new SimulatedClock();
        var storage = new InMemoryMarketDataRepository(new InMemoryTradeStorage(), new InMemoryOrderBookStorage(), new InMemoryBarStorage());
        var bus = new MarketDataEventBus();
        var pipeline = new MarketPipeline(clock, new NoOpTimeAdvancer(), storage, bus);
        
        bus.SubBar(e => capturedBar = e);

        var baseTime = new DateTime(2026, 1, 15, 10, 0, 0, DateTimeKind.Utc);
        
        // 1. 거래 발생 (10:00:05)
        pipeline.IngestTrade("BTC", new TradeTick(baseTime.AddSeconds(5), 100m, 1m, true));
        Assert.Null(capturedBar);

        // 2. 시간이 1분 지나서 호가 발생 (10:01:05)
        var nextTime = baseTime.AddMinutes(1).AddSeconds(5);
        clock.SetUtc(nextTime);
        pipeline.IngestOrderBookTop("BTC", new OrderBookTop(nextTime, 100m, 1m, 101m, 1m));

        // Assert: 호가만 들어왔지만 시간이 지났으므로 이전 분 바가 생성되어야 함
        Assert.NotNull(capturedBar);
        Assert.Equal(baseTime, capturedBar.Value.BarTimeUtc);
    }

    [Fact]
    public void OnTrade_MultipleSymbols_ShouldAggregateSeparately()
    {
        // Arrange
        var capturedEvents = new List<BarClosedEvent>();
        var aggregator = new TradeToBarAggregator1m(capturedEvents.Add);
        var baseTime = new DateTime(2026, 1, 15, 10, 0, 0, DateTimeKind.Utc);

        // Act
        aggregator.OnTrade("BTC", new TradeTick(baseTime.AddSeconds(10), 50000m, 1m, true));
        aggregator.OnTrade("ETH", new TradeTick(baseTime.AddSeconds(20), 3000m, 10m, true));
        
        // BTC만 다음 분으로 넘김
        aggregator.OnTrade("BTC", new TradeTick(baseTime.AddMinutes(1), 50100m, 1m, true));

        // Assert
        Assert.Single(capturedEvents);
        Assert.Equal("BTC", capturedEvents[0].Symbol);
        
        // ETH도 flush
        aggregator.FlushIfMinutePassed("ETH", baseTime.AddMinutes(1));
        Assert.Equal(2, capturedEvents.Count);
        Assert.Equal("ETH", capturedEvents[1].Symbol);
    }
}
