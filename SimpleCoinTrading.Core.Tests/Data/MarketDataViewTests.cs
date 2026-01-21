using SimpleCoinTrading.Core.Data;
using SimpleCoinTrading.Core.Time;
using SimpleCoinTrading.Core.Time.Clocks;
using SimpleCoinTrading.Core.Time.TimeFlows;
using Xunit;

namespace SimpleCoinTrading.Core.Tests.Data;

public class MarketDataViewTests
{
    private readonly Resolution _m1 = Resolution.M1;
    private readonly Resolution _m5 = Resolution.M5;

    private (MarketDataView view, SimulatedClock clock, InMemoryMarketDataRepository storage) CreateSystem(int capacity = 1000)
    {
        var clock = new SimulatedClock();
        clock.SetUtc(DateTime.Parse("2026-01-15 14:00"));
        var bars = new InMemoryBarStorage(capacity);
        var trades = new InMemoryTradeStorage(capacity);
        var books = new InMemoryOrderBookStorage();
        var storage = new InMemoryMarketDataRepository(trades, books, bars);
        var view = new MarketDataView(clock, storage);
        return (view, clock, storage);
    }

    [Fact]
    public void AppendBar_And_GetLastBar_ShouldWork()
    {
        // Arrange
        var (view, _, storage) = CreateSystem();
        var bar = new Bar(DateTime.Parse("2026-01-15 14:00"), 100, 110, 90, 105, 1000);

        // Act
        storage.AppendBar("BTC", _m1, bar);
        var lastBar = view.GetLastBar("BTC", _m1);

        // Assert
        Assert.NotNull(lastBar);
        Assert.Equal(bar.TimeUtc, lastBar.Value.TimeUtc);
        Assert.Equal(bar.Close, lastBar.Value.Close);
    }

    [Fact]
    public void GetLastBar_ShouldReturnNull_WhenNoData()
    {
        // Arrange
        var (view, _, _) = CreateSystem();

        // Act
        var lastBar = view.GetLastBar("BTC", _m1);

        // Assert
        Assert.Null(lastBar);
    }

    [Fact]
    public void Data_ShouldBeSeparated_BySymbolAndResolution()
    {
        // Arrange
        var (view, _, storage) = CreateSystem();
        var btcBar = new Bar(DateTime.Parse("2026-01-15 14:00"), 100, 110, 90, 105, 1000);
        var ethBar = new Bar(DateTime.Parse("2026-01-15 14:00"), 2000, 2100, 1900, 2050, 500);

        // Act
        storage.AppendBar("BTC", _m1, btcBar);
        storage.AppendBar("ETH", _m1, ethBar);
        storage.AppendBar("BTC", _m5, btcBar with { Close = 106 });

        // Assert
        Assert.Equal(105, view.GetLastBar("BTC", _m1)?.Close);
        Assert.Equal(2050, view.GetLastBar("ETH", _m1)?.Close);
        Assert.Equal(106, view.GetLastBar("BTC", _m5)?.Close);
    }

    [Fact]
    public void GetBars_ShouldReturnCorrectSubset()
    {
        // Arrange
        var (view, _, storage) = CreateSystem();
        for (int i = 1; i <= 10; i++)
        {
            storage.AppendBar("BTC", _m1, new Bar(DateTime.Parse($"2026-01-15 14:{i:D2}"), i, i, i, i, i));
        }

        // Act
        var bars = view.GetBars("BTC", 5, _m1);

        // Assert
        Assert.Equal(5, bars.Count);
        Assert.Equal(6, bars[0].Close);
        Assert.Equal(10, bars[4].Close);
    }

    [Fact]
    public void GetBars_ShouldReturnEmpty_WhenNoData()
    {
        // Arrange
        var (view, _, _) = CreateSystem();

        // Act
        var bars = view.GetBars("BTC", 5, _m1);

        // Assert
        Assert.Empty(bars);
    }

    [Fact]
    public void NowUtc_ShouldReturnClockTime()
    {
        // Arrange
        var (view, clock, _) = CreateSystem();
        var now = DateTime.Parse("2026-01-15 14:50").ToUniversalTime();

        // Act
        clock.SetUtc(now);

        // Assert
        Assert.Equal(now, view.NowUtc);
    }

    [Fact]
    public async Task ThreadSafety_AppendBar_ShouldNotThrow()
    {
        // Arrange
        var (view, _, storage) = CreateSystem(capacity: 1000);
        int tasksCount = 10;
        int iterations = 100;

        // Act
        var tasks = Enumerable.Range(0, tasksCount).Select(t => Task.Run(() =>
        {
            for (int i = 0; i < iterations; i++)
            {
                storage.AppendBar($"SYM{t}", _m1, new Bar(DateTime.Now, i, i, i, i, i));
                storage.AppendBar("COMMON", _m1, new Bar(DateTime.Now, i, i, i, i, i));
            }
        }));

        await Task.WhenAll(tasks);

        // Assert
        var commonBars = view.GetBars("COMMON", 1000, _m1);
        Assert.True(commonBars.Count <= 1000);
    }

    [Fact]
    public void Trade_Functions_ShouldWork()
    {
        var (view, clock, storage) = CreateSystem();
        var now = DateTime.UtcNow;
        clock.SetUtc(now);

        storage.AppendTrade("BTC", new TradeTick(now.AddSeconds(-10), 100, 1, true));
        storage.AppendTrade("BTC", new TradeTick(now.AddSeconds(-5), 110, 2, false));
        storage.AppendTrade("BTC", new TradeTick(now, 105, 3, true));

        var recent = view.GetRecentTrades("BTC", 3);
        Assert.Equal(3, recent.Count);
        Assert.Equal(105, recent[2].Price);

        // window 가 7초이면, [now-7s, now] 범위이므로 -5s 와 0s 틱이 포함됨
        var summary = view.GetTradeSummary("BTC", TimeSpan.FromSeconds(7));
        Assert.Equal(5, summary.TotalVolume);
        Assert.Equal(3, summary.BuyVolume);
        Assert.Equal(2, summary.SellVolume);
        Assert.Equal(2, summary.TradeCount);

        // window 가 11초이면, [now-11s, now] 범위이므로 -10s, -5s, 0s 틱 모두 포함됨
        var summaryFull = view.GetTradeSummary("BTC", TimeSpan.FromSeconds(11));
        Assert.Equal(6, summaryFull.TotalVolume);
        Assert.Equal(4, summaryFull.BuyVolume);
        Assert.Equal(2, summaryFull.SellVolume);
        Assert.Equal(3, summaryFull.TradeCount);
    }

    [Fact]
    public void OrderBook_Functions_ShouldWork()
    {
        var (view, _, storage) = CreateSystem();
        var ob = new OrderBookTop(DateTime.UtcNow, 100, 1, 101, 2);

        storage.UpdateTopOfBook("BTC", ob);
        var last = view.GetLastOrderBookTop("BTC");

        Assert.Equal(100, last?.BestBidPrice);
        Assert.Equal(101, last?.BestAskPrice);
        Assert.True(view.HasSymbol("BTC"));
    }
}
