using SimpleCoinTrading;
using Microsoft.Extensions.Logging;
using SimpleCoinTrading.Core;
using SimpleCoinTrading.Core.Broker;
using SimpleCoinTrading.Core.Data;
using SimpleCoinTrading.Core.Logs;
using SimpleCoinTrading.Core.Orders;
using SimpleCoinTrading.Core.Time;
using SimpleCoinTrading.Core.Time.Clocks;
using SimpleCoinTrading.Core.Time.TimeFlows;
using SimpleCoinTrading.Data;

public sealed class PaperOrderTestAlgorithm : IAlgorithm
{
    private readonly bool _testRateLimit;
    public string Name => "PaperOrderTestAlgorithm";

    private IAlgorithmContext? _ctx;
    private readonly List<IDisposable> _subs = new();

    private string? _activeOrderId;

    public PaperOrderTestAlgorithm(bool testRateLimit = false)
    {
        _testRateLimit = testRateLimit;
    }
    
    public void Initialize(IAlgorithmContext ctx)
    {
        _ctx = ctx;

        _subs.Add(ctx.SubscribeBarClosed(OnBarClosed));

        // 체결/주문상태는 broker.Events로도 볼 수 있지만,
        // 전략이 직접 구독하고 싶다면 ctx가 broker를 노출하는 형태로 받으면 됨.
    }

    public void Run() { }

    public void Stop()
    {
        foreach (var s in _subs) s.Dispose();
        _subs.Clear();
    }

    private void OnBarClosed(BarClosedEvent e)
    {
        Console.WriteLine(e.Bar.ToString());
        
        // 심볼 1개만 테스트한다고 가정
        var sym = e.Symbol;

        // 이미 활성 주문이 있으면 중복 제출 방지
        if (_activeOrderId != null) return;

        var ob = _ctx!.Market.GetLastOrderBookTop(sym);
        if (ob is null) return;

        // ask보다 약간 높은 가격으로 넣어서 체결 유도(또는 ask 이하로 넣어 미체결 테스트도 가능)
        var limit = ob.Value.BestAskPrice;

        // 아주 작은 수량으로 테스트
        var qty = 0.0002m;

        _ = Task.Run(async () =>
        {
            var ack = await _ctx!.PlaceOrderAsync(new PlaceOrderRequest(
                Symbol: sym,
                Side: OrderSide.Buy,
                Type: OrderType.Limit,
                Quantity: qty,
                LimitPrice: limit,
                ClientOrderId: $"test-{DateTime.UtcNow:HHmmss}"
            ));

            if (!ack.Accepted || ack.OrderId is null)
            {
                Console.WriteLine($"[ALGO] order rejected: {ack.Message}");
                return;
            }

            _activeOrderId = ack.OrderId;
            Console.WriteLine($"[ALGO] placed LIMIT BUY {sym} qty={qty} px={limit} id={ack.OrderId}");

            if (_testRateLimit)
            {
                for (int i = 0; i < 10; i++)
                {
                    try
                    {
                        await _ctx.PlaceOrderAsync(new PlaceOrderRequest(
                            sym, OrderSide.Buy, OrderType.Limit, qty, limit
                        ));
                        Console.WriteLine("Order OK");
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine(ex.Message);
                    }
                }
            }
            
            // 상태 폴링(테스트 편의) - 이벤트 기반만으로도 가능하지만, v1은 폴링도 OK
            for (int i = 0; i < 50; i++)
            {
                var st = await _ctx.GetOrderAsync(ack.OrderId);
                if (st is null) break;

                if (st.Status is OrderStatus.Filled or OrderStatus.Canceled or OrderStatus.Rejected)
                {
                    Console.WriteLine($"[ALGO] done: {st.Status} filled={st.FilledQuantity}/{st.Quantity} avg={st.AvgFillPrice}");
                    _activeOrderId = null;
                    return;
                }

                await Task.Delay(200);
            }

            // 오래 걸리면 취소
            await _ctx.CancelAsync(ack.OrderId);
            _activeOrderId = null;

            
        });
    }
}


internal static class Program
{
    static async Task Main()
    {
        using var cts = new CancellationTokenSource();
        Console.CancelKeyPress += (_, e) =>
        {
            e.Cancel = true;
            cts.Cancel();
            Console.WriteLine("Stopping...");
        };

        // 0) config
        var symbols = new[] { "KRW-BTC" };
        var startUtc = new DateTime(2026, 1, 15, 0, 0, 0, DateTimeKind.Utc);

        // 1) clock
        SimulatedClock clock = new SimulatedClock();
        clock.SetUtc(startUtc);
        
        // 2) repo (in-memory read+write)
        var repo = new InMemoryMarketDataRepository(
            bars: new InMemoryBarStorage(capacityPerSeries: 50_000),
            trades: new InMemoryTradeStorage(capacityPerSymbol: 200_000),
            books: new InMemoryOrderBookStorage()
        );

        // 3) bus
        var bus = new MarketDataEventBus();

        // 3.5) TimeFlow (ITimeAdvancer)
        var virtualClock = new VirtualClock();
        virtualClock.SetUtc(startUtc);
        var timeFlow = new DualModeTimeFlow(virtualClock, TimeFlowMode.Backtest);

        // 4) pipeline (write-only)
        var pipeline = new MarketPipeline(clock, timeFlow, repo /* write */, bus);

        // 5) view (read-only)
        IMarketDataView view = new MarketDataView(clock, repo /* read */);

        // 6) broker (paper)
        // subscribeOrderBook는 bus의 구독 메서드를 "함수로 래핑"해서 주입
        IBroker broker = new PaperBroker(
            name: "Paper",
            market: view,
            subscribeOrderBook: handler => bus.SubBook(handler),
            initialKrw: 1_000_000_000m,
            slippageBps: 1.0m,
            latency: TimeSpan.FromMilliseconds(20)
        );
        await broker.StartAsync(cts.Token);

        // broker 이벤트 로그
        broker.Events.Subscribe(new ActionObserver<BrokerEvent>(e =>
        {
            switch (e)
            {
                case OrderUpdatedEvent ou:
                    Console.WriteLine($"[BROKER] {ou.Order.Symbol} {ou.Order.OrderId} {ou.Order.Status} filled={ou.Order.FilledQuantity}/{ou.Order.Quantity} avg={ou.Order.AvgFillPrice}");
                    break;
                case FillEvent fe:
                    Console.WriteLine($"[FILL] {fe.Fill.Symbol} {fe.Fill.Side} {fe.Fill.Quantity}@{fe.Fill.Price} fee={fe.Fill.Fee}");
                    break;
                case BrokerErrorEvent be:
                    Console.WriteLine($"[BROKER-ERR] {be.Message}");
                    break;
            }
        }));

        using var loggerFactory = LoggerFactory.Create(builder =>
        {
            builder.AddConsole();
        });

        // 7) algorithm context (너 구현에 맞게)
        // 예: ctx가 Market/Clock/Bus/Broker를 모두 제공한다고 가정
        var algoLogHub = new AlgorithmLogHub();
        var orchestrator = new DelegatingOrchestrator(
            clock,
            broker, 
            new TradingGuard(), 
            new PerAlgorithmRateLimiterFactory(clock, 10),
            new InMemoryIdempotencyStore(),
            new InMemoryOrderIdMap(),
            new InMemoryOrderOwnershipStore(),
            loggerFactory.CreateLogger<DelegatingOrchestrator>(),
            algoLogHub);
        
        var ctx = new AlgorithmContext(
            view, 
            clock, 
            bus, 
            orchestrator,
            new AlgorithmLoggerFactory(algoLogHub));

        // 8) algorithm
        var algorithm = new PaperOrderTestAlgorithm(true); // 아래 예시
        algorithm.Initialize(ctx);
        algorithm.Run();

        // 9) market data source (synthetic)
        // IMarketDataSource source = new SyntheticMarketDataSource(
        //     pipeline: pipeline,
        //     symbols: symbols,
        //     startUtc: startUtc,
        //     tradeIntervalMs: 200,
        //     bookIntervalMs: 100
        // );
        IMarketDataSource source = new BithumbWebSocketMarketDataSource(
            pipeline,
            symbols
        );

        Console.WriteLine("Running simulation (Ctrl+C to stop)...");
        await source.RunAsync(cts.Token);
        await Task.Delay(Timeout.Infinite, cts.Token);
        // 10) shutdown
        algorithm.Stop();
        await broker.StopAsync(cts.Token);
        Console.WriteLine("Done.");
    }
}
