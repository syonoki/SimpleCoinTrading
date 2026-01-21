using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using SimpleCoinTrading.Core.Broker;

public sealed record TradingSnapshot(
    long Seq,
    DateTime TimeUtc,
    bool KillSwitchEnabled,
    IReadOnlyList<OrderState> Orders,
    IReadOnlyList<Fill> RecentFills,
    IReadOnlyList<Position> Positions,
    IReadOnlyList<AlgorithmRuntimeState> Algorithms,
    bool MarketDataOk,
    string MarketDataStatus
);

public sealed record AlgorithmRuntimeState(string Name, string Status, string Message);

public sealed class TradingState
{
    private long _seq;

    // ===== 핵심 상태 =====
    private readonly ConcurrentDictionary<string, OrderState> _orders = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, Position> _positions = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, AlgorithmRuntimeState> _algorithms = new(StringComparer.OrdinalIgnoreCase);

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
    
    public TradingState(int maxRecentFills = 200)
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

        // positions (현물 기준 아주 단순 버전)
        // - BUY: qty 증가, 평단 갱신
        // - SELL: qty 감소, 평단 유지(혹은 감소 후 0이면 제거)
        _positions.AddOrUpdate(
            fill.Symbol,
            key => CreatePositionFromFirstFill(fill),
            (key, old) => UpdatePosition(old, fill)
        );

        return NextSeq();
    }

    public long ApplyAlgorithmState(string name, string status, string message)
    {
        _algorithms[name] = new AlgorithmRuntimeState(name, status, message);
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
            Positions: _positions.Values.OrderBy(p => p.Symbol).ToList(),
            Algorithms: _algorithms.Values.OrderBy(a => a.Name).ToList(),
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

    private static Position CreatePositionFromFirstFill(Fill f)
    {
        if (f.Side == OrderSide.Buy)
            return new Position(f.Symbol, f.Quantity, f.Price);

        // 매도 fill이 첫 이벤트로 들어오는 경우는 비정상이지만(초기화/재시작 상황), 방어적으로 처리
        return new Position(f.Symbol, -f.Quantity, f.Price);
    }

    private static Position UpdatePosition(Position old, Fill f)
    {
        if (f.Side == OrderSide.Buy)
        {
            var newQty = old.Quantity + f.Quantity;
            if (newQty == 0m) return new Position(old.Symbol, 0m, 0m);

            // 가중평균 평단
            var newAvg = ((old.Quantity * old.AvgPrice) + (f.Quantity * f.Price)) / newQty;
            return new Position(old.Symbol, newQty, newAvg);
        }
        else
        {
            var newQty = old.Quantity - f.Quantity;
            if (newQty <= 0m)
            {
                // 0 이하가 되면 포지션 제거 대신 0으로 둠(단순 버전)
                return new Position(old.Symbol, 0m, old.AvgPrice);
            }

            return new Position(old.Symbol, newQty, old.AvgPrice);
        }
    }
}