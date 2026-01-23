using System.Collections.Concurrent;
using SimpleCoinTrading.Core.Broker;

namespace SimpleCoinTrading.Core.Orders;

public sealed record TradingSnapshot(
    long Seq,
    DateTime TimeUtc,
    bool KillSwitchEnabled,
    IReadOnlyList<OrderState> Orders,
    IReadOnlyList<Fill> RecentFills,

    bool MarketDataOk,
    string MarketDataStatus
);


public sealed class OrderStateProjection
{
    private long _seq;

    // ===== 핵심 상태 =====
    private readonly ConcurrentDictionary<string, OrderState> _orders = new(StringComparer.OrdinalIgnoreCase);

    // 최근 체결 N개만 보관 (UI/디버그용)
    private readonly ConcurrentQueue<Fill> _recentFills = new();
    private readonly int _maxRecentFills;

    // ===== 운영/가시성 =====
    public volatile bool MarketDataOk = true;
    public volatile string MarketDataStatus = "OK";

    // kill switch
    private volatile int _killSwitch; // 0/1

    public bool KillSwitchEnabled => Volatile.Read(ref _killSwitch) == 1;

    public long SetKillSwitch(bool enabled)
    {
        Interlocked.Exchange(ref _killSwitch, enabled ? 1 : 0);
        return NextSeq();
    }
    
    public OrderStateProjection(int maxRecentFills = 200)
    {
        _maxRecentFills = Math.Max(10, maxRecentFills);
    }

    public long NextSeq() => Interlocked.Increment(ref _seq);
    public long CurrentSeq => Volatile.Read(ref _seq);

    // ===== 이벤트 적용(API) =====

    public long ApplyOrderUpdated(OrderState order)
    {
        _orders[order.OrderId] = order;
        return NextSeq();
    }

    public long ApplyFill(Fill fill)
    {
        // fills
        _recentFills.Enqueue(fill);
        TrimRecentFills();

        return NextSeq();
    }



    // ===== 스냅샷 =====

    public TradingSnapshot Snapshot()
    {
        return new TradingSnapshot(
            Seq: CurrentSeq,
            KillSwitchEnabled: KillSwitchEnabled,
            TimeUtc: DateTime.UtcNow,
            Orders: _orders.Values.OrderByDescending(o => o.UpdatedUtc ?? o.CreatedUtc).ToList(),
            RecentFills: _recentFills.ToArray().Reverse().ToList(), // 최신이 앞에 오게
            MarketDataOk: MarketDataOk,
            MarketDataStatus: MarketDataStatus
        );
    }

    // ===== helpers =====

    private void TrimRecentFills()
    {
        while (_recentFills.Count > _maxRecentFills && _recentFills.TryDequeue(out _))
        {
        }
    }
}