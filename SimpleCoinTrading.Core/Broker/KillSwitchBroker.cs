namespace SimpleCoinTrading.Core.Broker;
public sealed class KillSwitchBroker : IBroker
{
    private readonly IBroker _inner;
    private readonly TradingState _state;

    public KillSwitchBroker(IBroker inner, TradingState state)
    {
        _inner = inner;
        _state = state;
    }

    // 이벤트는 그대로 통과
    public Task<AccountSnapshot> GetAccountAsync(CancellationToken ct = default) => _inner.GetAccountAsync(ct);

    public IObservable<BrokerEvent> Events => _inner.Events;
    public Task CancelAllAsync(CancellationToken contextCancellationToken) => _inner.CancelAllAsync(contextCancellationToken);

    public string Name => _inner.Name;
    public Task StartAsync(CancellationToken ct) => _inner.StartAsync(ct);
    public Task StopAsync(CancellationToken ct) => _inner.StopAsync(ct);

    public Task<OrderAck> PlaceOrderAsync(PlaceOrderRequest req, CancellationToken ct)
    {
        if (_state.KillSwitchEnabled)
        {
            Console.WriteLine($"[KillSwitch] Blocked order: {req.Symbol} {req.Side} {req.Quantity}");
            throw new InvalidOperationException("KillSwitch is ON: new orders are blocked.");
        }

        return _inner.PlaceOrderAsync(req, ct);
    }

    public Task<CancelAck> CancelOrderAsync(CancelOrderRequest req, CancellationToken ct = default) => _inner.CancelOrderAsync(req, ct);

    public Task<OrderState?> GetOrderAsync(string orderId, CancellationToken ct = default) => _inner.GetOrderAsync(orderId, ct);

    public Task<IReadOnlyList<OrderState>> GetOpenOrdersAsync(string symbol, CancellationToken ct = default) => _inner.GetOpenOrdersAsync(symbol, ct);

    public Task<Position?> GetPositionAsync(string symbol, CancellationToken ct = default) => _inner.GetPositionAsync(symbol, ct);
}
