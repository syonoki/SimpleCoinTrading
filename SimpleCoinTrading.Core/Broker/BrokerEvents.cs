namespace SimpleCoinTrading.Core.Broker;

public abstract record BrokerEvent(DateTime TimeUtc);

public sealed record OrderUpdatedEvent(DateTime TimeUtc, OrderState Order) : BrokerEvent(TimeUtc);

public sealed record FillEvent(DateTime TimeUtc, Fill Fill) : BrokerEvent(TimeUtc);

public sealed record BrokerErrorEvent(DateTime TimeUtc, string Message, string? Code = null) : BrokerEvent(TimeUtc);
