using SimpleCoinTrading.Core.Broker;
using SimpleCoinTrading.Core.Data;
using SimpleCoinTrading.Core.Logs;
using SimpleCoinTrading.Core.Orders;
using SimpleCoinTrading.Core.Time.Clocks;

namespace SimpleCoinTrading.Core;

public sealed class AlgorithmContext : IAlgorithmContext
{
    public IMarketDataView Market { get; }
    public IClock Clock { get; }

    public IAlgorithmLogger GetLogger(string algorithmId) => _logFactory.Create(algorithmId);

    private readonly MarketDataEventBus _bus;
    private readonly IOrderOrchestrator _orderOrchestrator;
    private readonly IAlgorithmLoggerFactory _logFactory;

    public AlgorithmContext(IMarketDataView market, 
        IClock clock, MarketDataEventBus bus,
        IOrderOrchestrator orderOrchestrator,
        IAlgorithmLoggerFactory logFactory)
    {
        Market = market;
        Clock = clock;
        _bus = bus;
        _orderOrchestrator = orderOrchestrator;
        _logFactory = logFactory;
    }

    public Task<OrderState?> GetOrderAsync(string orderId, CancellationToken ct = default)
    {
        return _orderOrchestrator.GetOrderAsync(orderId, ct);
    }

    public Task<OrderAck> PlaceOrderAsync(PlaceOrderRequest request, CancellationToken ct = default)
    {
        return _orderOrchestrator.PlaceOrderAsync(request, ct);
    }

    public Task CancelAsync(string orderId, CancellationToken ct = default)
    {
        return _orderOrchestrator.CancelAsync(orderId, ct);
    }

    public IDisposable SubscribeBarClosed(Action<BarClosedEvent> h) => _bus.SubBar(h);
    public IDisposable SubscribeTrade(Action<TradeTickEvent> h) => _bus.SubTrade(h);
    public IDisposable SubscribeOrderBook(Action<OrderBookTopEvent> h) => _bus.SubBook(h);

    public IDisposable Schedule(TimeSpan interval, Action tick)
    {
        throw new NotImplementedException();
    }
}