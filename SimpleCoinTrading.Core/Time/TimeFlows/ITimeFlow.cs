

using System.Threading.Channels;

namespace SimpleCoinTrading.Core.Time.TimeFlows;

public readonly record struct TimeTick(DateTime UtcNow);

/* <summary>
데이터 흐름이 시간의 흐름이기 때문에 MarketPipeline에서 시간을 제어한다
TradingHostedService
   ├─ timeFlow.Start(ct)             (생명주기)
   └─ await foreach (tick in timeFlow.Ticks) → aggregator.OnTime(tick)

MarketDataSource (WS/CSV/Synthetic)
   └─ emits MarketEvent(trade/orderbook/...)

MarketPipeline
   ├─ timeAdvancer.AdvanceTo(evt.TimeUtc)   (시간 진행)
   └─ dispatch evt to aggregators/stores/strategies

 </summary>*/
public interface ITimeFlow {
    ChannelReader<TimeTick> Ticks { get; }
    void Start(CancellationToken ct);
}

public interface ITimeAdvancer
{
    void AdvanceTo(DateTime marketUtc);
}