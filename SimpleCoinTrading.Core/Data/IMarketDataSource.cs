using SimpleCoinTrading.Core.Time;
using SimpleCoinTrading.Core.Time.Clocks;
using SimpleCoinTrading.Core.Time.TimeFlows;

namespace SimpleCoinTrading.Core.Data;
public sealed class MarketPipeline
{
    private readonly IClock _clock;
    private readonly ITimeAdvancer _timeAdvancer;
    private readonly IMarketDataStorage _write;
    private readonly MarketDataEventBus _bus;
    private readonly TradeToBarAggregator1m _agg;

    public MarketPipeline(
        IClock clock,
        ITimeAdvancer timeAdvancer,
        IMarketDataStorage write, MarketDataEventBus bus)
    {
        _clock = clock;
        _timeAdvancer = timeAdvancer;
        _write = write;
        _bus = bus;

        _agg = new TradeToBarAggregator1m(e =>
        {
            if (_clock is ISettableClock sc) sc.SetUtc(e.BarTimeUtc);
            _timeAdvancer.AdvanceTo(e.BarTimeUtc);
            _write.AppendBar(e.Symbol, e.Resolution, e.Bar);
            _bus.Publish(e);
        });
    }

    public void IngestTrade(string symbol, in TradeTick tick)
    {
        if (_clock is ISettableClock sc) sc.SetUtc(tick.TimeUtc);
        _timeAdvancer.AdvanceTo(tick.TimeUtc);

        _write.AppendTrade(symbol, tick);
        _bus.Publish(new TradeTickEvent(symbol, tick));
        _agg.OnTrade(symbol, tick);
    }

    public void IngestOrderBookTop(string symbol, in OrderBookTop top)
    {
        if (_clock is ISettableClock sc) sc.SetUtc(top.TimeUtc);
        _timeAdvancer.AdvanceTo(top.TimeUtc);

        _write.UpdateTopOfBook(symbol, top);
        _bus.Publish(new OrderBookTopEvent(symbol, top));

        // 호가 업데이트 시에도 시간이 흘렀을 수 있으므로 체크
        _agg.FlushIfMinutePassed(symbol, _clock.UtcNow);
    }

    public void FlushBars()
    {
        _agg.FlushAll(_clock.UtcNow);
    }
}


public interface IMarketDataSource
{
    Task RunAsync(CancellationToken ct);
}

public static class MarketPipelineExtensions
{
    public static void FlushBarsIfSupported(this MarketPipeline pipeline)
    {
        // MarketPipeline에 FlushBars()가 있으면 호출, 없으면 아무것도 안 함
        var m = typeof(MarketPipeline).GetMethod("FlushBars");
        m?.Invoke(pipeline, null);
    }
}
