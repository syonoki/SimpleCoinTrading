using SimpleCoinTrading.Core.Data;
using SimpleCoinTrading.Core.Test;
using SimpleCoinTrading.Core.Time;
using SimpleCoinTrading.Core.Time.Clocks;
using SimpleCoinTrading.Core.Time.TimeFlows;
using Xunit;

namespace SimpleCoinTrading.Core.Tests.Test;

public class SyntheticMarketDataSourceTests
{
    private class NoOpTimeAdvancer : ITimeAdvancer
    {
        public void AdvanceTo(DateTime marketUtc) { }
    }

    [Fact]
    public async Task RunAsync_ShouldGenerateData()
    {
        var clock = new SimulatedClock();
        var storage = new InMemoryMarketDataRepository(new InMemoryTradeStorage(), new InMemoryOrderBookStorage(), new InMemoryBarStorage());
        var bus = new MarketDataEventBus();
        var pipeline = new MarketPipeline(clock, new NoOpTimeAdvancer(), storage, bus);
        
        var source = new SyntheticMarketDataSource(pipeline, new[] { "BTC" }, DateTime.UtcNow, tradeIntervalMs: 10, bookIntervalMs: 10);
        
        using var cts = new CancellationTokenSource();
        var runTask = source.RunAsync(cts.Token);
        
        // Wait a bit for some data to be generated
        await Task.Delay(100);
        cts.Cancel();
        
        try { await runTask; } catch (OperationCanceledException) { }
        
        var trades = storage.GetRecentTrades("BTC", 100);
        var ob = storage.GetLastOrderBookTop("BTC");
        
        Assert.NotEmpty(trades);
        Assert.NotNull(ob);
    }
}
