using SimpleCoinTrading.Core.Time;
using SimpleCoinTrading.Core.Time.TimeFlows;

namespace SimpleCoinTrading.Core.Data;

public interface ITradeStorage
{
    void AppendTrade(string symbol, in TradeTick tick);
}

public interface IOrderBookStorage
{
    void UpdateTopOfBook(string symbol, in OrderBookTop top);
}

public interface IBarStorage
{
    void AppendBar(string symbol, Resolution resolution, in Bar bar);
}

public interface IMarketDataStorage :
    ITradeStorage,
    IOrderBookStorage,
    IBarStorage
{
}
/// <summary>
/// Read-only view of market data.
/// </summary>
public interface IMarketDataReadStorage
{
    // Bars
    IReadOnlyList<Bar> GetBars(string symbol, int size, Resolution res);
    Bar? GetLastBar(string symbol, Resolution res);

    // Trades
    IReadOnlyList<TradeTick> GetRecentTrades(string symbol, int maxCount);

    // OrderBook
    OrderBookTop? GetLastOrderBookTop(string symbol);
}
