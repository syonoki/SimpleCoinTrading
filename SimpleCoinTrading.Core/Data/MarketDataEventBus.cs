namespace SimpleCoinTrading.Core.Data;

public sealed class MarketDataEventBus
{
    private event Action<BarClosedEvent>? _bar;
    private event Action<TradeTickEvent>? _trade;
    private event Action<OrderBookTopEvent>? _book;

    public IDisposable SubBar(Action<BarClosedEvent> h)
    { _bar += h; return new Unsub(() => _bar -= h); }

    public IDisposable SubTrade(Action<TradeTickEvent> h)
    { _trade += h; return new Unsub(() => _trade -= h); }

    public IDisposable SubBook(Action<OrderBookTopEvent> h)
    { _book += h; return new Unsub(() => _book -= h); }

    public void Publish(in BarClosedEvent e) => _bar?.Invoke(e);
    public void Publish(in TradeTickEvent e) => _trade?.Invoke(e);
    public void Publish(in OrderBookTopEvent e) => _book?.Invoke(e);

    private sealed class Unsub : IDisposable
    {
        private Action? _d;
        public Unsub(Action d) => _d = d;
        public void Dispose() { _d?.Invoke(); _d = null; }
    }
}
