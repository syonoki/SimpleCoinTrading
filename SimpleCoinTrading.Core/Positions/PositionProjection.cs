using System.Collections.Concurrent;
using System.Reactive.Subjects;
using SimpleCoinTrading.Core.Broker;
using SimpleCoinTrading.Core.Orders;

namespace SimpleCoinTrading.Core.Positions;

public readonly record struct PositionKey(string AlgorithmId, string Symbol);

public sealed class PositionState
{
    public string AlgorithmId { get; init; } = default!;
    public string Symbol { get; init; } = default!;

    public decimal NetQty { get; set; } // + long, - short
    public decimal AvgPrice { get; set; } // 평균단가 (abs 기준)
    public decimal RealizedPnl { get; set; } // 누적 실현손익 (quote)
    public decimal LastPrice { get; set; } // 현재가
    public decimal UnrealizedPnl { get; set; } // 평가손익 (계산값)

    public DateTimeOffset UpdatedAt { get; set; }
}

public sealed record PositionChanged(PositionState Position, bool Removed);


public sealed class PositionProjection
{
    private readonly ConcurrentDictionary<PositionKey, PositionState> _pos = new();
    private readonly IOrderOwnershipStore _ownership; // orderId -> algoId
    private readonly object _gate = new(); // MVP: 계산은 lock 하나로 직렬화(안전)
    
    private readonly Subject<PositionChanged> _changes = new();
    public IObservable<PositionChanged> Changes => _changes;

    public PositionProjection(IOrderOwnershipStore ownership)
    {
        _ownership = ownership;
    }

    public IReadOnlyCollection<PositionState> Snapshot(string? algorithmId = null)
    {
        if (string.IsNullOrWhiteSpace(algorithmId))
            return _pos.Values.ToArray();

        return _pos.Values.Where(p => p.AlgorithmId == algorithmId).ToArray();
    }

    public bool TryGet(string algorithmId, string symbol, out PositionState state)
        => _pos.TryGetValue(new PositionKey(Norm(algorithmId), symbol), out state!);

    public IEnumerable<PositionState> OnFill(FillEvent fe)
    {
        // 소유 알고리즘 확인
        var algoId = _ownership.TryGetOwner(fe.Fill.OrderId, out var a) ? a : "UNKNOWN";
        var key = new PositionKey(algoId, fe.Fill.Symbol);

        lock (_gate)
        {
            var p = _pos.GetOrAdd(key, _ => new PositionState
            {
                AlgorithmId = algoId,
                Symbol = fe.Fill.Symbol,
                NetQty = 0m,
                AvgPrice = 0m,
                RealizedPnl = 0m,
                LastPrice = 0m,
                UnrealizedPnl = 0m,
                UpdatedAt = fe.TimeUtc
            });

            ApplyFillAvgCost(p, fe);

            // tick이 이미 들어온 경우 Unrealized 갱신
            RecalcUnrealized(p);

            p.UpdatedAt = fe.TimeUtc;
            return new[] { p };
        }
    }

    public IEnumerable<PositionState> OnTick(string symbol, decimal lastPrice, DateTimeOffset time)
    {
        lock (_gate)
        {
            // 심볼이 같은 포지션들(알고리즘별)을 모두 업데이트
            var affected = _pos.Values.Where(p => p.Symbol == symbol).ToList();
            foreach (var p in affected)
            {
                p.LastPrice = lastPrice;
                RecalcUnrealized(p);
                p.UpdatedAt = time;
            }
            return affected;
        }
    }

    private static void ApplyFillAvgCost(PositionState p, FillEvent fe)
    {
        var dq = fe.Fill.Side == OrderSide.Buy ? fe.Fill.Quantity : -fe.Fill.Quantity; // +buy, -sell
        var px = fe.Fill.Price;
        var fee = fe.Fill.Fee; // quote 통화라고 가정(MVP)

        var q0 = p.NetQty;
        var q1 = q0 + dq;

        // 1) 기존 포지션이 0이면 단순 진입
        if (q0 == 0m)
        {
            p.NetQty = q1;
            p.AvgPrice = px;
            p.RealizedPnl -= fee;
            return;
        }

        // 2) 같은 방향으로 늘어남 (avg 갱신)
        if (SameSign(q0, dq))
        {
            var abs0 = Math.Abs(q0);
            var absd = Math.Abs(dq);
            var abs1 = abs0 + absd;

            p.AvgPrice = (abs0 * p.AvgPrice + absd * px) / abs1;
            p.NetQty = q1;
            p.RealizedPnl -= fee;
            return;
        }

        // 3) 반대 방향(청산/감산/리버설)
        var closed = Math.Min(Math.Abs(q0), Math.Abs(dq));

        // 실현손익 계산
        // long(q0>0)에서 sell(dq<0): (px - avg)*closed
        // short(q0<0)에서 buy(dq>0): (avg - px)*closed
        var realized = q0 > 0m ? (px - p.AvgPrice) * closed
                               : (p.AvgPrice - px) * closed;

        p.RealizedPnl += realized;
        p.RealizedPnl -= fee;

        p.NetQty = q1;

        // 3-1) 완전 청산
        if (q1 == 0m)
        {
            p.AvgPrice = 0m;
            return;
        }

        // 3-2) 방향이 바뀜(리버설) → 남은 수량은 새 진입, avg = px
        if (!SameSign(q1, q0))
        {
            p.AvgPrice = px;
        }
        // 3-3) 부분 청산(방향 유지) → avg 유지
    }

    private static void RecalcUnrealized(PositionState p)
    {
        if (p.NetQty == 0m || p.LastPrice == 0m || p.AvgPrice == 0m)
        {
            p.UnrealizedPnl = 0m;
            return;
        }

        // long: (last-avg)*qty
        // short: (avg-last)*abs(qty)
        p.UnrealizedPnl = p.NetQty > 0m
            ? (p.LastPrice - p.AvgPrice) * p.NetQty
            : (p.AvgPrice - p.LastPrice) * Math.Abs(p.NetQty);
    }

    private static bool SameSign(decimal a, decimal b) => (a > 0m && b > 0m) || (a < 0m && b < 0m);
    private static string Norm(string s) => string.IsNullOrWhiteSpace(s) ? "UNKNOWN" : s;
    
    public void HandleFill(FillEvent fe)
    {
        var changed = OnFill(fe); // IEnumerable<PositionState>

        foreach (var p in changed)
        {
            var removed = p.NetQty == 0m;
            _changes.OnNext(new PositionChanged(p, removed));
        }
    }

    public void HandleTick(string symbol, decimal last, DateTimeOffset time)
    {
        var changed = OnTick(symbol, last, time); // IEnumerable<PositionState>

        foreach (var p in changed)
        {
            // 보통 0 포지션은 굳이 push 안 해도 됨(원하면 removed로 쏘면 됨)
            if (p.NetQty == 0m) continue;

            _changes.OnNext(new PositionChanged(p, false));
        }
    }
}
