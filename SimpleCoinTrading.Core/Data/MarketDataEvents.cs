using SimpleCoinTrading.Core.Time.TimeFlows;

namespace SimpleCoinTrading.Core.Data;

public readonly record struct OrderBookTopEvent(string Symbol, OrderBookTop Book);

public readonly record struct TradeTickEvent(string Symbol, TradeTick Tick);

public readonly record struct BarClosedEvent(string Symbol, Resolution Resolution, DateTime BarTimeUtc, Bar Bar);