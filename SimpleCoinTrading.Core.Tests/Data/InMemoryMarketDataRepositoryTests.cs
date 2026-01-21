using SimpleCoinTrading.Core.Data;
using SimpleCoinTrading.Core.Time;
using SimpleCoinTrading.Core.Time.TimeFlows;
using Xunit;

namespace SimpleCoinTrading.Core.Tests.Data;

public class InMemoryMarketDataRepositoryTests
{
    [Fact]
    public void BarStorage_ShouldHandleMultipleSeries()
    {
        var storage = new InMemoryBarStorage(10);
        var bar1 = new Bar(DateTime.UtcNow, 100, 110, 90, 105, 1000);
        var bar2 = new Bar(DateTime.UtcNow, 200, 210, 190, 205, 2000);

        storage.AppendBar("BTC", Resolution.M1, bar1);
        storage.AppendBar("BTC", Resolution.M5, bar2);

        Assert.Equal(105, storage.GetLastBar("BTC", Resolution.M1)?.Close);
        Assert.Equal(205, storage.GetLastBar("BTC", Resolution.M5)?.Close);
    }

    [Fact]
    public void TradeStorage_ShouldRespectCapacity()
    {
        var storage = new InMemoryTradeStorage(2);
        storage.AppendTrade("BTC", new TradeTick(DateTime.UtcNow, 1, 1, true));
        storage.AppendTrade("BTC", new TradeTick(DateTime.UtcNow, 2, 1, true));
        storage.AppendTrade("BTC", new TradeTick(DateTime.UtcNow, 3, 1, true));

        var recent = storage.GetRecent("BTC", 10);
        Assert.Equal(2, recent.Count);
        Assert.Equal(2, recent[0].Price);
        Assert.Equal(3, recent[1].Price);
    }

    [Fact]
    public void OrderBookStorage_ShouldReturnSymbols()
    {
        var storage = new InMemoryOrderBookStorage();
        storage.UpdateTopOfBook("BTC", new OrderBookTop(DateTime.UtcNow, 1, 1, 2, 1));
        storage.UpdateTopOfBook("ETH", new OrderBookTop(DateTime.UtcNow, 10, 1, 11, 1));

        var symbols = storage.Symbols.ToList();
        Assert.Contains("BTC", symbols);
        Assert.Contains("ETH", symbols);
    }

    [Fact]
    public void Repository_ShouldDelegateCalls()
    {
        var trades = new InMemoryTradeStorage();
        var books = new InMemoryOrderBookStorage();
        var bars = new InMemoryBarStorage();
        var repo = new InMemoryMarketDataRepository(trades, books, bars);

        repo.AppendTrade("BTC", new TradeTick(DateTime.UtcNow, 50000, 1, true));
        repo.UpdateTopOfBook("BTC", new OrderBookTop(DateTime.UtcNow, 49990, 1, 50010, 1));
        repo.AppendBar("BTC", Resolution.M1, new Bar(DateTime.UtcNow, 50000, 50100, 49900, 50050, 10));

        Assert.Single(repo.GetRecentTrades("BTC", 10));
        Assert.NotNull(repo.GetLastOrderBookTop("BTC"));
        Assert.NotNull(repo.GetLastBar("BTC", Resolution.M1));
    }
}
