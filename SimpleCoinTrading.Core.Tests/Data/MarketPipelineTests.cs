using SimpleCoinTrading.Core.Data;
using SimpleCoinTrading.Core.Time;
using SimpleCoinTrading.Core.Time.Clocks;
using SimpleCoinTrading.Core.Time.TimeFlows;
using Xunit;

namespace SimpleCoinTrading.Core.Tests.Data;

public class MarketPipelineTests
{
    private class NoOpTimeAdvancer : ITimeAdvancer
    {
        public void AdvanceTo(DateTime marketUtc) { }
    }

    [Fact]
    public void IngestTrade_ShouldUpdateStorageAndBusAndAggregator()
    {
        var clock = new SimulatedClock();
        var trades = new InMemoryTradeStorage();
        var books = new InMemoryOrderBookStorage();
        var bars = new InMemoryBarStorage();
        var storage = new InMemoryMarketDataRepository(trades, books, bars);
        var bus = new MarketDataEventBus();
        
        TradeTickEvent? busReceived = null;
        bus.SubTrade(e => busReceived = e);
        
        var pipeline = new MarketPipeline(clock, new NoOpTimeAdvancer(), storage, bus);
        
        var time = new DateTime(2026, 1, 15, 10, 0, 5, DateTimeKind.Utc);
        var tick = new TradeTick(time, 50000m, 1.5m, true);
        
        pipeline.IngestTrade("BTC", tick);
        
        // Assert storage
        var recent = storage.GetRecentTrades("BTC", 1);
        Assert.Single(recent);
        Assert.Equal(50000m, recent[0].Price);
        
        // Assert bus
        Assert.NotNull(busReceived);
        Assert.Equal("BTC", busReceived.Value.Symbol);
        
        // Assert clock update
        Assert.Equal(time, clock.UtcNow);
    }

    [Fact]
    public void IngestOrderBookTop_ShouldUpdateStorageAndBus()
    {
        var clock = new SimulatedClock();
        var trades = new InMemoryTradeStorage();
        var books = new InMemoryOrderBookStorage();
        var bars = new InMemoryBarStorage();
        var storage = new InMemoryMarketDataRepository(trades, books, bars);
        var bus = new MarketDataEventBus();
        
        OrderBookTopEvent? busReceived = null;
        bus.SubBook(e => busReceived = e);
        
        var pipeline = new MarketPipeline(clock, new NoOpTimeAdvancer(), storage, bus);
        
        var time = new DateTime(2026, 1, 15, 10, 0, 5, DateTimeKind.Utc);
        var top = new OrderBookTop(time, 100, 1, 101, 1);
        
        pipeline.IngestOrderBookTop("BTC", top);
        
        // Assert storage
        var last = storage.GetLastOrderBookTop("BTC");
        Assert.NotNull(last);
        Assert.Equal(100, last.Value.BestBidPrice);
        
        // Assert bus
        Assert.NotNull(busReceived);
        Assert.Equal("BTC", busReceived.Value.Symbol);
        
        // Assert clock update
        Assert.Equal(time, clock.UtcNow);
    }

    [Fact]
    public void IngestTrade_ShouldTriggerBarPublication_WhenMinuteChanges()
    {
        var clock = new SimulatedClock();
        var storage = new InMemoryMarketDataRepository(new InMemoryTradeStorage(), new InMemoryOrderBookStorage(), new InMemoryBarStorage());
        var bus = new MarketDataEventBus();
        
        BarClosedEvent? barReceived = null;
        bus.SubBar(e => barReceived = e);
        
        var pipeline = new MarketPipeline(clock, new NoOpTimeAdvancer(), storage, bus);
        
        var time1 = new DateTime(2026, 1, 15, 10, 0, 5, DateTimeKind.Utc);
        pipeline.IngestTrade("BTC", new TradeTick(time1, 50000m, 1m, true));
        
        Assert.Null(barReceived);
        
        var time2 = new DateTime(2026, 1, 15, 10, 1, 5, DateTimeKind.Utc);
        pipeline.IngestTrade("BTC", new TradeTick(time2, 51000m, 1m, true));
        
        Assert.NotNull(barReceived);
        Assert.Equal("BTC", barReceived.Value.Symbol);
        Assert.Equal(new DateTime(2026, 1, 15, 10, 0, 0, DateTimeKind.Utc), barReceived.Value.BarTimeUtc);
        
        // Also check if bar is in storage
        var storedBar = storage.GetLastBar("BTC", Resolution.M1);
        Assert.NotNull(storedBar);
        Assert.Equal(50000m, storedBar.Value.Open);
    }
}
