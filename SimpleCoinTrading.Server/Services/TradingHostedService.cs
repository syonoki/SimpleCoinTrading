using SimpleCoinTrading.Core;
using SimpleCoinTrading.Core.Algorithms;
using SimpleCoinTrading.Core.Broker;
using SimpleCoinTrading.Core.Data;
using SimpleCoinTrading.Core.Orders;
using SimpleCoinTrading.Core.Utils;
using SimpleCoinTrading.Data;
using SimpleCoinTrading.Server.Algorithms;
using SimpleCoinTrading.Server.Services.Orders;


namespace SimpleCoinTrading.Server.Services;

public sealed class TradingHostedService : BackgroundService
{
    private readonly ILogger<TradingHostedService> _log;
    private readonly OrderStateProjection _state;
    private readonly ServerEventHub _hub;

    // 너가 만든 것들(인터페이스/구현체)
    private readonly IBroker _broker;
    private readonly IMarketDataSource _marketSource;
    private readonly IAlgorithmEngine _engine;

    private IDisposable? _brokerSub;

    public TradingHostedService(
        ILogger<TradingHostedService> log,
        OrderStateProjection state,
        ServerEventHub hub,
        IBroker broker,
        IMarketDataSource marketSource,
        IAlgorithmEngine engine)
    {
        _log = log;
        _state = state;
        _hub = hub;
        _broker = broker;
        _marketSource = marketSource;
        _engine = engine;
    }

    public override async Task StartAsync(CancellationToken cancellationToken)
    {
        _log.LogInformation("TradingHostedService starting...");

        // 1) Broker 이벤트 -> TradingState + EventHub로 투영(Projection)
        _brokerSub = _broker.Events.Subscribe(new ActionObserver<BrokerEvent>(e =>
        {
            try
            {
                var seq = 0L;

                switch (e)
                {
                    case OrderUpdatedEvent ou:
                        seq = _state.ApplyOrderUpdated(ou.Order);
                        _hub.Publish(ServerEventFactory.FromOrder(seq, ou));
                        break;

                    case SimpleCoinTrading.Core.Broker.FillEvent fe:
                        seq = _state.ApplyFill(fe.Fill);
                        _hub.Publish(ServerEventFactory.FromFill(seq, fe));
                        break;

                    case BrokerErrorEvent be:
                        seq = _state.NextSeq();
                        _hub.Publish(ServerEventFactory.FromSystem(seq, "ERROR", be.Message));
                        break;
                }
            }
            catch (Exception ex)
            {
                _log.LogError(ex, "Error while projecting broker event");
            }
        }));

        // 2) 각 컴포넌트 start
        await _broker.StartAsync(cancellationToken);

        await _marketSource.RunAsync(cancellationToken); // WS 시작

        await base.StartAsync(cancellationToken);

        _log.LogInformation("TradingHostedService started.");
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // 여기가 “콘솔 Main에서 while 돌던 자리”야.
        // MarketDataSource가 내부 루프(Task)로 돈다면, 여기서는 서비스가 살아있게 유지하면 됨.
        // (RunAsync 패턴이면 여기서 await RunAsync 하면 됨)

        try
        {
            while (!stoppingToken.IsCancellationRequested)
            {
                var snap = _state.Snapshot();
                _log.LogInformation(
                    "STATE seq={Seq} orders={Orders} fills={Fills} market={Market}",
                    snap.Seq, snap.Orders.Count, snap.RecentFills.Count, snap.MarketDataStatus);

                await Task.Delay(TimeSpan.FromSeconds(5), stoppingToken);
            }
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "ExecuteAsync crashed");
        }
    }

    public override async Task StopAsync(CancellationToken cancellationToken)
    {
        _log.LogInformation("TradingHostedService stopping...");

        // stop 순서: 데이터 수신 중지 -> 엔진 중지 -> 브로커 중지
        // IMarketDataSource에 StopAsync가 없으면 구현을 확인해야 함 (BithumbWebSocketMarketDataSource에는 있음)
        if (_marketSource is BithumbWebSocketMarketDataSource bw) await bw.StopAsync();
        _engine.StopAll();
        try
        {
            await _broker.StopAsync(cancellationToken);
        }
        catch
        {
        }

        _brokerSub?.Dispose();

        _log.LogInformation("TradingHostedService stopped.");
        await base.StopAsync(cancellationToken);
    }
}