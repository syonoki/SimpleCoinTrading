namespace SimpleCoinTrading.Core.Events;

public abstract record OrderEvent : TradingEvent
{
    public string OrderId { get; init; } = default!;
    public string Symbol { get; init; } = default!;
}

public sealed record OrderSubmitted : OrderEvent
{
    public decimal Qty { get; init; }
    public override string Summary
        => $"Order submitted {Symbol} {Qty}";
}

public sealed record OrderFilled : OrderEvent
{
    public decimal FilledQty { get; init; }
    public decimal Price { get; init; }

    public override string Summary
        => $"Order filled {Symbol} {FilledQty} @ {Price}";
}