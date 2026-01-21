namespace SimpleCoinTrading.Core.Events;

public enum OrderSide
{
    Buy,
    Sell
}

public abstract record OrderEvent : TradingEvent
{
    // Stable client-side id used for tracking across retries
    public string ClientOrderId { get; init; } = default!;

    // Exchange-assigned id (may be null until accepted)
    public string? ExchangeOrderId { get; init; }

    public string Symbol { get; init; } = default!;

    // Deterministic reconstruction / ordering
    public DateTime EventUtc { get; init; }
}

public sealed record OrderSubmitted : OrderEvent
{
    public OrderSide Side { get; init; }
    public decimal Qty { get; init; }

    public override string Summary
        => $"Order submitted {Symbol} {Side} {Qty}";
}

public sealed record OrderAccepted : OrderEvent
{
    public override string Summary
        => $"Order accepted {Symbol} {ClientOrderId} -> {ExchangeOrderId}";
}

public sealed record OrderRejected : OrderEvent
{
    public string Reason { get; init; } = default!;

    public override string Summary
        => $"Order rejected {Symbol} {ClientOrderId} ({Reason})";
}

public sealed record OrderCanceled : OrderEvent
{
    public string? Reason { get; init; }

    public override string Summary
        => $"Order canceled {Symbol} {ClientOrderId}";
}

public sealed record OrderFilled : OrderEvent
{
    // Used for idempotency (dedupe fills). Typically exchange execution/trade id.
    public string ExecutionId { get; init; } = default!;

    public OrderSide Side { get; init; }

    public decimal FilledQty { get; init; }
    public decimal Price { get; init; }

    // Fee in the specified currency (often quote currency)
    public decimal Fee { get; init; }
    public string FeeCurrency { get; init; } = default!;

    public override string Summary
        => $"Order filled {Symbol} {Side} {FilledQty} @ {Price} (fee {Fee} {FeeCurrency})";
}