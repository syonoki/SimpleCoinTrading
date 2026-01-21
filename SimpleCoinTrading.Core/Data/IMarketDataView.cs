using SimpleCoinTrading.Core.Time;
using SimpleCoinTrading.Core.Time.TimeFlows;

namespace SimpleCoinTrading.Core.Data;

public readonly record struct Bar(
    DateTime TimeUtc,
    decimal Open,
    decimal High,
    decimal Low,
    decimal Close,
    decimal Volume
);


public record struct TradeTick(
    DateTime TimeUtc,
    decimal Price,
    decimal Quantity,
    bool IsBuy
);

public readonly record struct TradeSummary(
    DateTime FromUtc,
    DateTime ToUtc,
    decimal TotalVolume,
    decimal BuyVolume,
    decimal SellVolume,
    int TradeCount
);

public readonly record struct OrderBookTop(
    DateTime TimeUtc,
    decimal BestBidPrice,
    decimal BestBidQuantity,
    decimal BestAskPrice,
    decimal BestAskQuantity
)
{
    public decimal Spread => BestAskPrice - BestBidPrice;
    public decimal MidPrice => (BestBidPrice + BestAskPrice) / 2m;
}


// =========================
// MarketDataView (read-only)
// =========================
public interface IMarketDataView
{
    DateTime NowUtc { get; }

    IReadOnlyList<Bar> GetBars(string symbol, int size, Resolution res);
    Bar? GetLastBar(string symbol, Resolution res);

    IReadOnlyList<TradeTick> GetRecentTrades(string symbol, int maxCount);

    OrderBookTop? GetLastOrderBookTop(string symbol);
    TradeSummary GetTradeSummary(string symbol, TimeSpan window);
    bool HasSymbol(string symbol);
}