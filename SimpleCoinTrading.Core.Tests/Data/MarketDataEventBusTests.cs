using SimpleCoinTrading.Core.Data;
using SimpleCoinTrading.Core.Time;
using SimpleCoinTrading.Core.Time.TimeFlows;
using Xunit;

namespace SimpleCoinTrading.Core.Tests.Data;

public class MarketDataEventBusTests
{
    [Fact]
    public void SubBar_ShouldReceivePublishedEvent()
    {
        var bus = new MarketDataEventBus();
        BarClosedEvent? received = null;
        using var sub = bus.SubBar(e => received = e);

        var e = new BarClosedEvent("BTC", Resolution.M1, DateTime.UtcNow, new Bar(DateTime.UtcNow, 1, 2, 3, 4, 5));
        bus.Publish(e);

        Assert.NotNull(received);
        Assert.Equal("BTC", received.Value.Symbol);
    }

    [Fact]
    public void SubTrade_ShouldReceivePublishedEvent()
    {
        var bus = new MarketDataEventBus();
        TradeTickEvent? received = null;
        using var sub = bus.SubTrade(e => received = e);

        var e = new TradeTickEvent("BTC", new TradeTick(DateTime.UtcNow, 100, 1, true));
        bus.Publish(e);

        Assert.NotNull(received);
        Assert.Equal(100, received.Value.Tick.Price);
    }

    [Fact]
    public void SubBook_ShouldReceivePublishedEvent()
    {
        var bus = new MarketDataEventBus();
        OrderBookTopEvent? received = null;
        using var sub = bus.SubBook(e => received = e);

        var e = new OrderBookTopEvent("BTC", new OrderBookTop(DateTime.UtcNow, 10, 1, 11, 1));
        bus.Publish(e);

        Assert.NotNull(received);
        Assert.Equal(10, received.Value.Book.BestBidPrice);
    }

    [Fact]
    public void Dispose_ShouldUnsubscribe()
    {
        var bus = new MarketDataEventBus();
        int count = 0;
        var sub = bus.SubBar(e => count++);

        bus.Publish(new BarClosedEvent("BTC", Resolution.M1, DateTime.UtcNow, default));
        Assert.Equal(1, count);

        sub.Dispose();
        bus.Publish(new BarClosedEvent("BTC", Resolution.M1, DateTime.UtcNow, default));
        Assert.Equal(1, count);
    }
}
