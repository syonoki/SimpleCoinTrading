using SimpleCoinTrading.Core.Time;
using SimpleCoinTrading.Core.Time.TimeFlows;

namespace SimpleCoinTrading.Core.Data;

// =========================
// Stores (state)
// =========================
public sealed class InMemoryBarStorage : IBarStorage
{
    private readonly int _capacity;
    private readonly Dictionary<(string Symbol, Resolution Res), RingBuffer<Bar>> _series = new();
    private readonly object _lock = new();

    public InMemoryBarStorage(int capacityPerSeries = 50_000) => _capacity = capacityPerSeries;

    public void AppendBar(string symbol, Resolution res, in Bar bar)
    {
        RingBuffer<Bar> rb;
        var key = (symbol, res);

        lock (_lock)
        {
            if (!_series.TryGetValue(key, out rb!))
            {
                rb = new RingBuffer<Bar>(_capacity);
                _series[key] = rb;
            }
        }

        rb.Add(bar);
    }

    public IReadOnlyList<Bar> GetBars(string symbol, int size, Resolution res)
    {
        var key = (symbol, res);
        lock (_lock)
        {
            if (!_series.TryGetValue(key, out var rb)) return Array.Empty<Bar>();
            return rb.Tail(size);
        }
    }

    public Bar? GetLastBar(string symbol, Resolution res)
    {
        var key = (symbol, res);
        lock (_lock)
        {
            if (!_series.TryGetValue(key, out var rb)) return null;
            return rb.LastOrDefault();
        }
    }
}

public sealed class InMemoryTradeStorage : ITradeStorage
{
    private readonly int _capacity;
    private readonly Dictionary<string, RingBuffer<TradeTick>> _ticks = new(StringComparer.OrdinalIgnoreCase);
    private readonly object _lock = new();

    public InMemoryTradeStorage(int capacityPerSymbol = 200_000) => _capacity = capacityPerSymbol;

    public void AppendTrade(string symbol, in TradeTick tick)
    {
        RingBuffer<TradeTick> rb;
        lock (_lock)
        {
            if (!_ticks.TryGetValue(symbol, out rb!))
            {
                rb = new RingBuffer<TradeTick>(_capacity);
                _ticks[symbol] = rb;
            }
        }

        rb.Add(tick);
    }

    public IReadOnlyList<TradeTick> GetRecent(string symbol, int maxCount)
    {
        lock (_lock)
        {
            if (!_ticks.TryGetValue(symbol, out var rb)) return Array.Empty<TradeTick>();
            return rb.Tail(maxCount);
        }
    }
}

public sealed class InMemoryOrderBookStorage : IOrderBookStorage
{
    private readonly Dictionary<string, OrderBookTop> _last = new(StringComparer.OrdinalIgnoreCase);
    private readonly object _lock = new();

    public IEnumerable<string> Symbols
    {
        get
        {
            lock (_lock) return _last.Keys.ToArray();
        }
    }

    public void UpdateTopOfBook(string symbol, in OrderBookTop top)
    {
        lock (_lock) _last[symbol] = top;
    }

    public OrderBookTop? GetLast(string symbol)
    {
        lock (_lock)
        {
            if (_last.TryGetValue(symbol, out var v)) return v;
            return null;
        }
    }
}

public sealed class InMemoryMarketDataRepository : IMarketDataStorage, IMarketDataReadStorage
{
    private readonly InMemoryTradeStorage _trades;
    private readonly InMemoryOrderBookStorage _books;
    private readonly InMemoryBarStorage _bars;

    public InMemoryMarketDataRepository(
        InMemoryTradeStorage trades,
        InMemoryOrderBookStorage books,
        InMemoryBarStorage bars)
    {
        _trades = trades;
        _books = books;
        _bars = bars;
    }

    public void AppendTrade(string symbol, in TradeTick tick)
        => _trades.AppendTrade(symbol, tick);

    public void UpdateTopOfBook(string symbol, in OrderBookTop top)
        => _books.UpdateTopOfBook(symbol, top);

    public void AppendBar(string symbol, Resolution resolution, in Bar bar)
        => _bars.AppendBar(symbol, resolution, bar);

    // ===== Read =====
    public IReadOnlyList<Bar> GetBars(string symbol, int size, Resolution res) => _bars.GetBars(symbol, size, res);
    public Bar? GetLastBar(string symbol, Resolution res) => _bars.GetLastBar(symbol, res);

    public IReadOnlyList<TradeTick> GetRecentTrades(string symbol, int maxCount) => _trades.GetRecent(symbol, maxCount);

    public OrderBookTop? GetLastOrderBookTop(string symbol) => _books.GetLast(symbol);
}