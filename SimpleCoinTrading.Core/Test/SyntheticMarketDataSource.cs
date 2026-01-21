using SimpleCoinTrading.Core.Data;

namespace SimpleCoinTrading.Core.Test;

public class SyntheticMarketDataSource : IMarketDataSource
{
    private readonly MarketPipeline _pipeline;
    private readonly IReadOnlyList<string> _symbols;
    private readonly DateTime _startUtc;
    private readonly int _tradeIntervalMs;
    private readonly int _bookIntervalMs;

    public SyntheticMarketDataSource(
        MarketPipeline pipeline,
        IReadOnlyList<string> symbols,
        DateTime startUtc,
        int tradeIntervalMs = 200,
        int bookIntervalMs = 100)
    {
        _pipeline = pipeline;
        _symbols = symbols;
        _startUtc = startUtc;
        _tradeIntervalMs = tradeIntervalMs;
        _bookIntervalMs = bookIntervalMs;
    }

    public async Task RunAsync(CancellationToken ct)
    {
        var rng = new Random(7);
        var px = _symbols.ToDictionary(s => s, _ => 50_000m);

        DateTime t = _startUtc;
        DateTime nextTrade = t;
        DateTime nextBook = t;

        while (!ct.IsCancellationRequested)
        {
            // 가상 시간 진행(실제 sleep은 짧게)
            t = t.AddMilliseconds(50);

            // orderbook (자주)
            if (t >= nextBook)
            {
                foreach (var sym in _symbols)
                {
                    var mid = px[sym];
                    var spread = 1m + (decimal)rng.NextDouble() * 2m; // 1~3
                    var bid = mid - spread / 2m;
                    var ask = mid + spread / 2m;

                    var top = new OrderBookTop(
                        TimeUtc: t,
                        BestBidPrice: bid, BestBidQuantity: 1.2m,
                        BestAskPrice: ask, BestAskQuantity: 1.1m
                    );

                    _pipeline.IngestOrderBookTop(sym, top);
                }

                nextBook = nextBook.AddMilliseconds(_bookIntervalMs);
            }

            // trades
            if (t >= nextTrade)
            {
                foreach (var sym in _symbols)
                {
                    var last = px[sym];
                    var shock = (decimal)(rng.NextDouble() - 0.5) * 0.002m; // +-0.1%
                    var price = Math.Max(1m, last * (1m + shock));
                    var qty = (decimal)rng.NextDouble() * 0.5m + 0.01m;

                    px[sym] = price;

                    var tick = new TradeTick(TimeUtc: t, Price: price, Quantity: qty, qty > 0);
                    _pipeline.IngestTrade(sym, tick);
                }

                nextTrade = nextTrade.AddMilliseconds(_tradeIntervalMs);
            }

            // 바 강제 플러시(틱이 끊긴 경우 대비)
            _pipeline.FlushBars();

            await Task.Delay(5, ct);
        }
    }
}