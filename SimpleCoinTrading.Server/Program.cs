using SimpleCoinTrading.Core;
using SimpleCoinTrading.Core.Algorithms;
using SimpleCoinTrading.Core.Broker;
using SimpleCoinTrading.Core.Data;
using SimpleCoinTrading.Core.Logs;
using SimpleCoinTrading.Core.Orders;
using SimpleCoinTrading.Core.Positions;
using SimpleCoinTrading.Core.Time.Clocks;
using SimpleCoinTrading.Core.Time.TimeFlows;
using SimpleCoinTrading.Data;
using SimpleCoinTrading.Server;
using SimpleCoinTrading.Server.Services;
using SimpleCoinTrading.Server.Services.Orders;
using SimpleCoinTrading.Server.Services.Positions;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.
builder.Services.AddGrpc();

// Core / Infra 컴포넌트들
builder.Services.AddSingleton<MarketPipeline>(provider =>
{
    var clock = provider.GetRequiredService<IClock>();
    var repo = provider.GetRequiredService<IMarketDataReadStorage>() as IMarketDataStorage;
    var bus = provider.GetRequiredService<MarketDataEventBus>();
    
    // Server mode typically uses LiveTimeFlow or a no-op if real-time clock is used.
    // Since we're in Program.cs top-level, we can't easily define a class here without placing it at the end.
    // Let's use a simple lambda-based implementation if possible, but ITimeAdvancer is an interface.
    
    return new MarketPipeline(clock, new ServerTimeAdvancer(), repo!, bus);
});

builder.Services.AddSingleton<ITimeAdvancer, ServerTimeAdvancer>();

builder.Services.AddSingleton<IMarketDataSource, BithumbWebSocketMarketDataSource>(sp =>
{
    var pipeline = sp.GetRequiredService<MarketPipeline>();
    var marketDataSource = new BithumbWebSocketMarketDataSource(pipeline, new[] { "KRW-BTC", "KRW-ETH" });
    return marketDataSource;
});

builder.Services.AddSingleton<VirtualClock>();
builder.Services.AddSingleton<IClock>(sp => sp.GetRequiredService<VirtualClock>());

builder.Services.AddSingleton<DualModeTimeFlow>(provider =>
{
    return new DualModeTimeFlow(provider.GetRequiredService<VirtualClock>(), 
        TimeFlowMode.RealTimeReplay, TimeSpan.FromSeconds(1));
});
builder.Services.AddSingleton<ITimeFlow>(sp => sp.GetRequiredService<DualModeTimeFlow>());
builder.Services.AddSingleton<ITimeAdvancer>(sp => sp.GetRequiredService<DualModeTimeFlow>());

// market data storage
var tradeStorage = new InMemoryTradeStorage();
var orderBookStorage = new InMemoryOrderBookStorage();
var barStorage = new InMemoryBarStorage();
var dataRepository = new InMemoryMarketDataRepository(tradeStorage, orderBookStorage, barStorage);

builder.Services.AddSingleton<IMarketDataStorage, InMemoryMarketDataRepository>(provider => dataRepository);
builder.Services.AddSingleton<IMarketDataReadStorage, InMemoryMarketDataRepository>(provider => dataRepository);

builder.Services.AddSingleton<IMarketDataView>(sp => 
   new MarketDataView(sp.GetRequiredService<IClock>(), sp.GetRequiredService<IMarketDataReadStorage>()));

builder.Services.AddSingleton<PaperBroker>(provider =>
    new PaperBroker("Paper Broker", provider.GetRequiredService<IMarketDataView>(),
        handler => provider.GetRequiredService<MarketDataEventBus>().SubBook(handler)));

builder.Services.AddSingleton<IBroker>(sp =>
{
    var inner = sp.GetRequiredService<PaperBroker>();
    var state = sp.GetRequiredService<OrderStateProjection>();
    return new KillSwitchBroker(inner, state);
});

builder.Services.AddSingleton<PositionProjection>();
builder.Services.AddSingleton<IPositionUpdateHub, PositionUpdateHub>();

// 브릿지: Projection.Changes -> Hub publish 연결
builder.Services.AddHostedService<PositionProjectionHubBridge>();

builder.Services.AddSingleton<MarketDataEventBus>();
builder.Services.AddSingleton<ITradingGuard, TradingGuard>();
builder.Services.AddSingleton<IRateLimiter>(sp => 
    new PerSecondFixedWindowRateLimiter(sp.GetRequiredService<IClock>(), 10, "ServerLimit"));
builder.Services.AddSingleton<IIdempotencyStore, InMemoryIdempotencyStore>();
builder.Services.AddSingleton<IOrderIdMap, InMemoryOrderIdMap>();
builder.Services.AddSingleton<IRateLimiterFactory>(provider =>
    new PerAlgorithmRateLimiterFactory(provider.GetRequiredService<IClock>(), maxPerSecond: 5));
builder.Services.AddSingleton<IOrderOwnershipStore, InMemoryOrderOwnershipStore>();
builder.Services.AddSingleton<IAlgorithmLoggerFactory, AlgorithmLoggerFactory>();

builder.Services.AddSingleton<IOrderOrchestrator, DelegatingOrchestrator>();

builder.Services.AddSingleton<IAlgorithmContext, AlgorithmContext>();
builder.Services.AddSingleton<IAlgorithmEngine, AlgorithmEngine>();

builder.Services.AddSingleton<IAlgorithmLogSink>(
    _ => new AlgorithmFileLogSink(
        baseDir: Path.Combine(AppContext.BaseDirectory, "algo-logs")));

builder.Services.AddSingleton<IAlgorithmLogHub>(sp =>
{
    var sinks = sp.GetServices<IAlgorithmLogSink>();
    return new AlgorithmLogHub(capacityPerAlgo: 5000, sinks);
});

// 상태/이벤트 허브 DI
builder.Services.AddSingleton<OrderStateProjection>();
builder.Services.AddSingleton<ServerEventHub>();

builder.Services.AddSingleton<IAlgorithmLoggerFactory, AlgorithmLoggerFactory>();

builder.Services.AddSingleton<TradingHostedService>();
builder.Services.AddHostedService<TradingHostedService>();
builder.Services.AddHostedService<OrderLifecycleTracker>();


var app = builder.Build();

// Configure the HTTP request pipeline.
app.MapGrpcService<GreeterService>();
// gRPC endpoints
app.MapGrpcService<TradingControlService>();
app.MapGrpcService<AlgoLogGrpcService>();
app.MapGrpcService<PositionControlService>();

// health/ready (운영/모니터링)
app.MapGet("/health", () => Results.Ok(new { ok = true }));

app.MapGet("/ready", (OrderStateProjection state) =>
{
    // 최소 ready 기준: 마켓데이터 OK
    if (!state.MarketDataOk)
        return Results.StatusCode(StatusCodes.Status503ServiceUnavailable);

    return Results.Ok(new { ok = true, market = state.MarketDataStatus });
});

app.MapGet("/", () => "TradingService gRPC is running.");
app.Run();

namespace SimpleCoinTrading.Server
{
    public class ServerTimeAdvancer : ITimeAdvancer
    {
        public void AdvanceTo(DateTime marketUtc) { }
    }
}