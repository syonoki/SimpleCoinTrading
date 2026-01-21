namespace SimpleCoinTrading.Core.Events;

public sealed record SignalProposed : TradingEvent
{
    public string StrategyId { get; init; } = default!;
    public string Symbol { get; init; } = default!;
    public decimal ProposedQty { get; init; }
    public decimal Score { get; init; }

    public override string Summary
        => $"{StrategyId} → {Symbol} {ProposedQty:+0.####;-0.####;0}";
}