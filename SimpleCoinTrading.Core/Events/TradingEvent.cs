using SimpleCoinTrading.Core.Ids;

namespace SimpleCoinTrading.Core.Events;

public abstract record TradingEvent
{
    public BatchId BatchId { get; init; }
    public DateTimeOffset OccurredAt { get; init; } = DateTimeOffset.UtcNow;

    // 분산/비동기 추적용 (나중에 사용)
    public Guid CorrelationId { get; init; } = Guid.NewGuid();

    // UI / Audit에서 바로 쓰기 위한 짧은 설명
    public virtual string Summary => GetType().Name;
}