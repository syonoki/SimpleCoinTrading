using SimpleCoinTrading.Core.Time;

namespace SimpleCoinTrading.Core.Data;

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
