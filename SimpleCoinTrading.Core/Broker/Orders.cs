namespace SimpleCoinTrading.Core.Broker;

public enum OrderSide { Buy, Sell }

public enum OrderType { Market, Limit }

public enum OrderStatus
{
    New,            // created locally (optional)
    Accepted,       // accepted by broker/exchange
    PartiallyFilled,
    Filled,
    Canceled,
    Rejected,
    Expired
}

public enum TimeInForce
{
    GTC,    // Good Till Cancel
    IOC,    // Immediate Or Cancel
    FOK     // Fill Or Kill
}

public sealed record PlaceOrderRequest(
    string Symbol,                 // 예: "KRW-BTC"
    OrderSide Side,                // Buy / Sell
    OrderType Type,                // Market / Limit
    decimal Quantity,              // 수량
    decimal? LimitPrice = null,    // 지정가일 때만 사용
    string? ClientOrderId = null,  // 전략이 발급한 id (idempotency용)
    string? AlgorithmId = null,
    TimeInForce Tif = TimeInForce.GTC
);

public sealed record OrderAck(
    bool Accepted,
    string? OrderId,
    string? ClientOrderId,
    string? Message = null
);

public sealed record CancelOrderRequest(
    string OrderId,
    string? Symbol = null
);

public sealed record CancelAck(
    bool Accepted,
    string OrderId,
    string? Message = null
);

public sealed record OrderState(
    string OrderId,
    string Symbol,
    OrderSide Side,
    OrderType Type,
    OrderStatus Status,
    decimal Quantity,
    decimal FilledQuantity,
    decimal? LimitPrice,
    decimal? AvgFillPrice,
    DateTime CreatedUtc,
    DateTime? UpdatedUtc,
    string? ClientOrderId = null
);

public sealed record Fill(
    string OrderId,
    string Symbol,
    OrderSide Side,
    decimal Price,
    decimal Quantity,
    decimal Fee,               // 수수료(현물)
    string FeeCurrency,        // "KRW" or "BTC" ...
    DateTime TimeUtc,
    string? TradeId = null
);

public sealed record Position(
    string Symbol,
    decimal Quantity,
    decimal AvgPrice
);

public sealed record BalanceItem(
    string Currency,           // "KRW", "BTC" ...
    decimal Total,
    decimal Available
);

public sealed record AccountSnapshot(
    DateTime TimeUtc,
    IReadOnlyList<BalanceItem> Balances
);
