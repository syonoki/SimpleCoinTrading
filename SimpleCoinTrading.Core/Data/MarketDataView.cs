using SimpleCoinTrading.Core.Time;
using SimpleCoinTrading.Core.Time.Clocks;
using SimpleCoinTrading.Core.Time.TimeFlows;

namespace SimpleCoinTrading.Core.Data;

public sealed class MarketDataView : IMarketDataView
{
    private readonly IClock _clock;
    private readonly IMarketDataReadStorage _read;

    public MarketDataView(IClock clock, IMarketDataReadStorage read)
    {
        _clock = clock;
        _read = read;
    }

    public DateTime NowUtc => _clock.UtcNow;

    public IReadOnlyList<Bar> GetBars(string symbol, int size, Resolution res) => _read.GetBars(symbol, size, res);
    public Bar? GetLastBar(string symbol, Resolution res) => _read.GetLastBar(symbol, res);

    public IReadOnlyList<TradeTick> GetRecentTrades(string symbol, int maxCount) => _read.GetRecentTrades(symbol, maxCount);

    public OrderBookTop? GetLastOrderBookTop(string symbol) => _read.GetLastOrderBookTop(symbol);

    public TradeSummary GetTradeSummary(string symbol, TimeSpan window)
    {
        var now = NowUtc;
        var start = now - window;
        var ticks = _read.GetRecentTrades(symbol, 1000); // 넉넉히 가져옴. 실제로는 시간 범위 필터 필요

        decimal totalVol = 0;
        decimal buyVol = 0;
        decimal sellVol = 0;
        int count = 0;

        foreach (var t in ticks)
        {
            if (t.TimeUtc >= start && t.TimeUtc <= now)
            {
                totalVol += t.Quantity;
                if (t.IsBuy) buyVol += t.Quantity;
                else sellVol += t.Quantity;
                count++;
            }
        }

        return new TradeSummary(start, now, totalVol, buyVol, sellVol, count);
    }

    public bool HasSymbol(string symbol) => _read.GetLastOrderBookTop(symbol) != null;
}

