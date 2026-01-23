using System.Collections.Concurrent;
using SimpleCoinTrading.Core.Broker;
using SimpleCoinTrading.Core.Data;
using SimpleCoinTrading.Core.Logs;
using SimpleCoinTrading.Core.Orders;
using SimpleCoinTrading.Core.Time.Clocks;

namespace SimpleCoinTrading.Core.Algorithms;

public sealed class AlgoScope : IDisposable
{
    private readonly ConcurrentBag<IDisposable> _items = new();
    private int _disposed;

    public void Add(IDisposable d)
    {
        if (d is null) return;
        if (Volatile.Read(ref _disposed) == 1)
        {
            d.Dispose();
            return;
        }

        _items.Add(d);
        if (Volatile.Read(ref _disposed) == 1) Dispose();
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) == 1) return;
        while (_items.TryTake(out var d))
            try
            {
                d.Dispose();
            }
            catch
            {
            }
    }
}

public sealed class AlgorithmContext : IAlgorithmContext
{
    public string AlgorithmId { get; }
    public IMarketDataView Market { get; }
    public IClock Clock { get; }

    public IAlgorithmLogger GetLogger() => _logFactory.Create(AlgorithmId);

    private readonly AlgoScope _scope = new();
    private readonly MarketDataEventBus _bus;
    private readonly IOrderOrchestrator _orderOrchestrator;
    private readonly IAlgorithmLoggerFactory _logFactory;
    private readonly IAlgorithmLogger logger_;

    public AlgorithmContext(
            string algorithmId,
            IMarketDataView market,
            IClock clock, MarketDataEventBus bus,
            IOrderOrchestrator orderOrchestrator,
            IAlgorithmLoggerFactory logFactory)
    {
        AlgorithmId = algorithmId;
        Market = market;
        Clock = clock;
        _bus = bus;
        _orderOrchestrator = orderOrchestrator;
        _logFactory = logFactory;
        
        logger_ = GetLogger();
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

    public IDisposable SubscribeBarClosed(Action<BarClosedEvent> h)
    {
        logger_.Debug("Subscribing to bar closed event");
        var sub = _bus.SubBar(h);
        _scope.Add(sub);
        return sub;
    }

    public IDisposable SubscribeTrade(Action<TradeTickEvent> h)
    {
        logger_.Debug("Subscribing to trade tick event");
        var sub = _bus.SubTrade(h);
        _scope.Add(sub);
        return sub;
    }

    public IDisposable SubscribeOrderBook(Action<OrderBookTopEvent> h)
    {
        logger_.Debug("Subscribing to order book top event");
        var sub = _bus.SubBook(h);
        _scope.Add(sub);
        return sub;
    }

    public IDisposable Schedule(TimeSpan interval, Action tick)
    {
        throw new NotImplementedException();
    }

    public void Dispose()
    {
        logger_.Info("Algorithm disposed");
        _scope.Dispose();
    }
}