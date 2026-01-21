using SimpleCoinTrading.Core.Data;

namespace SimpleCoinTrading.Core.Broker;

// =========================
// PaperBroker implementation
// =========================

public sealed class PaperBroker : IBroker
{
    public string Name { get; }
    public IObservable<BrokerEvent> Events => _events;

    private readonly IMarketDataView _market;
    private readonly SimpleSubject<BrokerEvent> _events = new();

    private readonly object _lock = new();

    // balances
    private decimal _krwTotal;
    private decimal _krwAvailable;
    private decimal _krwReserved; // buy limit이 예약한 KRW

    // positions (심볼 단위로 들고감: "KRW-BTC" 같은 키)
    private readonly Dictionary<string, Position> _positions = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, decimal> _coinReserved = new(StringComparer.OrdinalIgnoreCase); // sell limit이 예약한 qty

    // orders
    private readonly Dictionary<string, OrderState> _orders = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, decimal> _remainingQty = new(StringComparer.OrdinalIgnoreCase); // 남은 수량
    private readonly Dictionary<string, decimal> _reservedKrwByOrder = new(StringComparer.OrdinalIgnoreCase); // order별 예약 KRW
    private readonly Dictionary<string, decimal> _reservedCoinByOrder = new(StringComparer.OrdinalIgnoreCase); // order별 예약 coin qty

    private readonly IDisposable _bookSub;

    private volatile bool _started;

    // execution params
    private readonly decimal _takerFeeRate;
    private readonly decimal _makerFeeRate;
    private readonly decimal _slippageBps;
    private readonly TimeSpan _latency;

    public PaperBroker(
        string name,
        IMarketDataView market,
        Action<Action<OrderBookTopEvent>> subscribeOrderBook,
        decimal initialKrw = 1_000_000_000m,
        decimal takerFeeRate = 0.0004m,
        decimal makerFeeRate = 0.0002m,
        decimal slippageBps = 0.0m,
        TimeSpan? latency = null)
    {
        Name = name;
        _market = market ?? throw new ArgumentNullException(nameof(market));

        _krwTotal = initialKrw;
        _krwAvailable = initialKrw;
        _krwReserved = 0m;

        _takerFeeRate = takerFeeRate;
        _makerFeeRate = makerFeeRate;
        _slippageBps = slippageBps;
        _latency = latency ?? TimeSpan.Zero;

        // ✅ orderbook 이벤트가 올 때마다 open order를 스캔해서 체결(부분체결 포함)
        subscribeOrderBook(OnOrderBookTop);
    }

    public Task StartAsync(CancellationToken ct = default) { _started = true; return Task.CompletedTask; }

    public Task StopAsync(CancellationToken ct = default) { _started = false; return Task.CompletedTask; }

    public async Task<OrderAck> PlaceOrderAsync(PlaceOrderRequest req, CancellationToken ct = default)
    {
        EnsureStarted();

        if (req.Quantity <= 0) return new OrderAck(false, null, req.ClientOrderId, "Quantity must be > 0.");
        if (req.Type == OrderType.Limit && (req.LimitPrice is null || req.LimitPrice <= 0))
            return new OrderAck(false, null, req.ClientOrderId, "LimitPrice must be set for limit orders.");

        if (_latency > TimeSpan.Zero) await Task.Delay(_latency, ct).ConfigureAwait(false);

        var now = _market.NowUtc;
        var id = Guid.NewGuid().ToString("N");

        var state = new OrderState(
            OrderId: id,
            Symbol: req.Symbol,
            Side: req.Side,
            Type: req.Type,
            Status: OrderStatus.Accepted,
            Quantity: req.Quantity,
            FilledQuantity: 0m,
            LimitPrice: req.LimitPrice,
            AvgFillPrice: null,
            CreatedUtc: now,
            UpdatedUtc: now,
            ClientOrderId: req.ClientOrderId
        );

        lock (_lock)
        {
            // 예약(Reserve) - 부분체결/미체결 동안 자금/수량을 잡아둠
            if (!TryReserveForOrder(state, out var reserveErr))
                return new OrderAck(false, null, req.ClientOrderId, reserveErr);

            _orders[id] = state;
            _remainingQty[id] = req.Quantity;
        }

        _events.OnNext(new OrderUpdatedEvent(now, state));

        // IOC/FOK/Market는 즉시 체결 시도(가능한 만큼 부분체결까지)
        if (req.Type == OrderType.Market || req.Tif is TimeInForce.IOC or TimeInForce.FOK)
        {
            TryMatchUsingTopOfBook(req.Symbol);
            // IOC/FOK는 남으면 취소 처리
            if (req.Tif is TimeInForce.IOC or TimeInForce.FOK)
            {
                lock (_lock)
                {
                    if (_orders.TryGetValue(id, out var o))
                    {
                        var rem = _remainingQty.TryGetValue(id, out var r) ? r : 0m;
                        if (rem > 0m && o.Status is OrderStatus.Accepted or OrderStatus.PartiallyFilled)
                            CancelInternal(id, $"TIF={req.Tif} not fully filled immediately.");
                    }
                }
            }
        }

        return new OrderAck(true, id, req.ClientOrderId, null);
    }

    public async Task<CancelAck> CancelOrderAsync(CancelOrderRequest req, CancellationToken ct = default)
    {
        EnsureStarted();
        if (_latency > TimeSpan.Zero) await Task.Delay(_latency, ct).ConfigureAwait(false);

        lock (_lock)
        {
            if (!_orders.TryGetValue(req.OrderId, out var o))
                return new CancelAck(false, req.OrderId, "Order not found.");

            if (o.Status is OrderStatus.Filled or OrderStatus.Canceled or OrderStatus.Rejected)
                return new CancelAck(false, req.OrderId, $"Cannot cancel order in state {o.Status}.");

            CancelInternal(req.OrderId, "Canceled by user.");
        }

        return new CancelAck(true, req.OrderId, null);
    }

    public async Task CancelAllAsync(CancellationToken ct = default)
    {
        EnsureStarted();
        if (_latency > TimeSpan.Zero) await Task.Delay(_latency, ct).ConfigureAwait(false);

        lock (_lock)
        {
            var openOrderIds = _orders.Values
                .Where(o => o.Status is OrderStatus.Accepted or OrderStatus.PartiallyFilled)
                .Select(o => o.OrderId)
                .ToList();

            foreach (var id in openOrderIds)
            {
                CancelInternal(id, "Canceled by CancelAll.");
            }
        }
    }

    public Task<OrderState?> GetOrderAsync(string orderId, CancellationToken ct = default)
    {
        lock (_lock)
        {
            _orders.TryGetValue(orderId, out var o);
            return Task.FromResult<OrderState?>(o);
        }
    }

    public Task<IReadOnlyList<OrderState>> GetOpenOrdersAsync(string symbol, CancellationToken ct = default)
    {
        lock (_lock)
        {
            var list = _orders.Values
                .Where(o => o.Symbol.Equals(symbol, StringComparison.OrdinalIgnoreCase))
                .Where(o => o.Status is OrderStatus.Accepted or OrderStatus.PartiallyFilled)
                .OrderBy(o => o.CreatedUtc)
                .ToList();

            return Task.FromResult<IReadOnlyList<OrderState>>(list);
        }
    }

    public Task<Position?> GetPositionAsync(string symbol, CancellationToken ct = default)
    {
        lock (_lock)
        {
            _positions.TryGetValue(symbol, out var p);
            return Task.FromResult<Position?>(p);
        }
    }

    public Task<AccountSnapshot> GetAccountAsync(CancellationToken ct = default)
    {
        lock (_lock)
        {
            var now = _market.NowUtc;

            var balances = new List<BalanceItem>
            {
                new BalanceItem("KRW", _krwTotal, _krwAvailable),
            };

            foreach (var kv in _positions)
            {
                var sym = kv.Key;
                var pos = kv.Value;
                var reserved = _coinReserved.TryGetValue(sym, out var r) ? r : 0m;
                var avail = Math.Max(0m, pos.Quantity - reserved);
                balances.Add(new BalanceItem(sym, pos.Quantity, avail));
            }

            return Task.FromResult(new AccountSnapshot(now, balances));
        }
    }

    // =========================
    // Orderbook handler -> match open orders (GTC + partial fills)
    // =========================
    private void OnOrderBookTop(OrderBookTopEvent e)
    {
        if (!_started) return;
        TryMatchUsingTopOfBook(e.Symbol);
    }

    private void TryMatchUsingTopOfBook(string symbol)
    {
        var ob = _market.GetLastOrderBookTop(symbol);
        if (ob is null) return;

        // 같은 심볼의 open order들을 오래된 순으로 처리
        List<string> openIds;
        lock (_lock)
        {
            openIds = _orders.Values
                .Where(o => o.Symbol.Equals(symbol, StringComparison.OrdinalIgnoreCase))
                .Where(o => o.Status is OrderStatus.Accepted or OrderStatus.PartiallyFilled)
                .OrderBy(o => o.CreatedUtc)
                .Select(o => o.OrderId)
                .ToList();
        }

        // top-of-book 잔량은 "한 틱"에 공유되는 유동성으로 보고, order가 consume함
        decimal askAvail = ob.Value.BestAskQuantity;
        decimal bidAvail = ob.Value.BestBidQuantity;

        foreach (var id in openIds)
        {
            OrderState o;
            decimal rem;
            lock (_lock)
            {
                if (!_orders.TryGetValue(id, out o)) continue;
                if (!_remainingQty.TryGetValue(id, out rem)) continue;
                if (rem <= 0m) continue;
            }

            // 체결 가능 여부 + 체결가
            if (o.Type == OrderType.Market)
            {
                // market: 가능한 만큼 즉시 체결 (top 잔량만큼)
                var fillQty = (o.Side == OrderSide.Buy)
                    ? Math.Min(rem, askAvail)
                    : Math.Min(rem, bidAvail);

                if (fillQty <= 0m) continue;

                var px = (o.Side == OrderSide.Buy) ? ob.Value.BestAskPrice : ob.Value.BestBidPrice;
                px = ApplySlippage(px, o.Side);
                var feeRate = _takerFeeRate;

                if (!TryFill(id, px, fillQty, feeRate))
                {
                    RejectInternal(id, "Insufficient funds/position during market fill.");
                    continue;
                }

                // consume liquidity
                if (o.Side == OrderSide.Buy) askAvail -= fillQty;
                else bidAvail -= fillQty;

                continue;
            }

            // limit
            var lp = o.LimitPrice!.Value;
            bool canFill = (o.Side == OrderSide.Buy) ? (lp >= ob.Value.BestAskPrice) : (lp <= ob.Value.BestBidPrice);
            if (!canFill) continue;

            var maxQty = (o.Side == OrderSide.Buy) ? askAvail : bidAvail;
            var qty = Math.Min(rem, maxQty);
            if (qty <= 0m) continue;

            var price = (o.Side == OrderSide.Buy) ? ob.Value.BestAskPrice : ob.Value.BestBidPrice;
            price = ApplySlippage(price, o.Side);

            // limit의 수수료율은 maker/taker 논쟁이 있는데, v2에선 makerFeeRate로 둠
            if (!TryFill(id, price, qty, _makerFeeRate))
            {
                RejectInternal(id, "Insufficient funds/position during limit fill.");
                continue;
            }

            if (o.Side == OrderSide.Buy) askAvail -= qty;
            else bidAvail -= qty;
        }
    }

    // =========================
    // Reserve / Fill / Cancel / Reject
    // =========================
    private bool TryReserveForOrder(OrderState o, out string? error)
    {
        error = null;

        // market은 예약을 “현재 top 기준”으로 잡되, v2는 market도 부분체결로 남을 수 있으니
        // 여기선 보수적으로 "현재 top 가격"으로 전액 예약.
        var ob = _market.GetLastOrderBookTop(o.Symbol);
        if (ob is null)
        {
            error = "No orderbook available for reserve.";
            return false;
        }

        var pxForReserve = o.Type == OrderType.Limit
            ? o.LimitPrice!.Value
            : (o.Side == OrderSide.Buy ? ob.Value.BestAskPrice : ob.Value.BestBidPrice);

        var feeRate = (o.Type == OrderType.Market) ? _takerFeeRate : _makerFeeRate;

        if (o.Side == OrderSide.Buy)
        {
            var notional = pxForReserve * o.Quantity;
            var fee = notional * feeRate;
            var reserve = notional + fee;

            if (_krwAvailable < reserve)
            {
                error = "Insufficient KRW.";
                return false;
            }

            _krwAvailable -= reserve;
            _krwReserved += reserve;
            _reservedKrwByOrder[o.OrderId] = reserve;
            return true;
        }
        else
        {
            // sell: 코인 보유/가용 체크
            if (!_positions.TryGetValue(o.Symbol, out var pos) || pos.Quantity <= 0m)
            {
                error = "No position to sell.";
                return false;
            }

            var alreadyRes = _coinReserved.TryGetValue(o.Symbol, out var r) ? r : 0m;
            var avail = pos.Quantity - alreadyRes;
            if (avail < o.Quantity)
            {
                error = "Insufficient available position (reserved by other orders).";
                return false;
            }

            _coinReserved[o.Symbol] = alreadyRes + o.Quantity;
            _reservedCoinByOrder[o.OrderId] = o.Quantity;
            return true;
        }
    }

    private bool TryFill(string orderId, decimal price, decimal fillQty, decimal feeRate)
    {
        lock (_lock)
        {
            if (!_orders.TryGetValue(orderId, out var o)) return false;
            if (o.Status is OrderStatus.Canceled or OrderStatus.Rejected or OrderStatus.Filled) return false;

            var now = _market.NowUtc;

            if (!_remainingQty.TryGetValue(orderId, out var rem) || rem <= 0m) return false;
            if (fillQty <= 0m) return true;

            fillQty = Math.Min(fillQty, rem);

            var notional = price * fillQty;
            var fee = notional * feeRate;

            if (o.Side == OrderSide.Buy)
            {
                // buy는 reserve에서 차감
                if (!_reservedKrwByOrder.TryGetValue(orderId, out var resKrw) || resKrw <= 0m)
                    return false;

                var spend = notional + fee;
                if (resKrw < spend)
                    return false;

                // reserve 감소
                resKrw -= spend;
                _reservedKrwByOrder[orderId] = resKrw;
                _krwReserved -= spend;
                // (이미 _krwAvailable에서 빼놨으니) total에서만 실제 지출 반영
                _krwTotal -= spend;

                // position 증가(평단 업데이트)
                var pos = _positions.TryGetValue(o.Symbol, out var p) ? p : new Position(o.Symbol, 0m, 0m);
                var newQty = pos.Quantity + fillQty;
                var newAvg = (newQty == 0m) ? 0m : ((pos.Quantity * pos.AvgPrice) + (fillQty * price)) / newQty;
                _positions[o.Symbol] = new Position(o.Symbol, newQty, newAvg);
            }
            else
            {
                // sell은 reserved coin에서 차감 + position 감소
                if (!_reservedCoinByOrder.TryGetValue(orderId, out var resCoin) || resCoin < fillQty)
                    return false;

                if (!_positions.TryGetValue(o.Symbol, out var pos) || pos.Quantity < fillQty)
                    return false;

                resCoin -= fillQty;
                _reservedCoinByOrder[orderId] = resCoin;

                var curRes = _coinReserved.TryGetValue(o.Symbol, out var rr) ? rr : 0m;
                _coinReserved[o.Symbol] = Math.Max(0m, curRes - fillQty);

                var remainPos = pos.Quantity - fillQty;
                if (remainPos <= 0m) _positions.Remove(o.Symbol);
                else _positions[o.Symbol] = new Position(o.Symbol, remainPos, pos.AvgPrice);

                // KRW 수익(수수료 차감 후)
                var proceeds = notional - fee;
                _krwAvailable += proceeds;
                _krwTotal += proceeds;
            }

            // order 상태 업데이트
            var newFilled = o.FilledQuantity + fillQty;

            decimal? newAvgFill;
            if (o.AvgFillPrice is null || o.FilledQuantity <= 0m)
                newAvgFill = price;
            else
                newAvgFill = ((o.AvgFillPrice.Value * o.FilledQuantity) + (price * fillQty)) / newFilled;

            rem -= fillQty;
            _remainingQty[orderId] = rem;

            var status = rem <= 0m ? OrderStatus.Filled : OrderStatus.PartiallyFilled;

            var updated = o with
            {
                Status = status,
                FilledQuantity = newFilled,
                AvgFillPrice = newAvgFill,
                UpdatedUtc = now
            };

            _orders[orderId] = updated;

            // 이벤트
            var fill = new Fill(
                OrderId: orderId,
                Symbol: o.Symbol,
                Side: o.Side,
                Price: price,
                Quantity: fillQty,
                Fee: fee,
                FeeCurrency: "KRW",
                TimeUtc: now,
                TradeId: Guid.NewGuid().ToString("N")
            );

            _events.OnNext(new FillEvent(now, fill));
            _events.OnNext(new OrderUpdatedEvent(now, updated));

            // 완전 체결이면 잔여 reserve/coin release
            if (status == OrderStatus.Filled)
                ReleaseRemainderLocked(orderId);

            return true;
        }
    }

    private void CancelInternal(string orderId, string reason)
    {
        var now = _market.NowUtc;

        if (!_orders.TryGetValue(orderId, out var o)) return;
        if (o.Status is OrderStatus.Filled or OrderStatus.Canceled or OrderStatus.Rejected) return;

        var canceled = o with { Status = OrderStatus.Canceled, UpdatedUtc = now };
        _orders[orderId] = canceled;

        ReleaseRemainderLocked(orderId);

        _events.OnNext(new OrderUpdatedEvent(now, canceled));
        _events.OnNext(new BrokerErrorEvent(now, $"Order canceled: {reason} (orderId={orderId})"));
    }

    private void RejectInternal(string orderId, string reason)
    {
        var now = _market.NowUtc;

        if (!_orders.TryGetValue(orderId, out var o)) return;
        if (o.Status is OrderStatus.Filled or OrderStatus.Canceled or OrderStatus.Rejected) return;

        var rej = o with { Status = OrderStatus.Rejected, UpdatedUtc = now };
        _orders[orderId] = rej;

        ReleaseRemainderLocked(orderId);

        _events.OnNext(new OrderUpdatedEvent(now, rej));
        _events.OnNext(new BrokerErrorEvent(now, $"Order rejected: {reason} (orderId={orderId})"));
    }

    private void ReleaseRemainderLocked(string orderId)
    {
        // buy: 남은 reserved KRW를 available로 되돌림
        if (_reservedKrwByOrder.TryGetValue(orderId, out var krw) && krw > 0m)
        {
            _reservedKrwByOrder.Remove(orderId);
            _krwReserved -= krw;
            _krwAvailable += krw;
        }

        // sell: 남은 reserved coin을 해제
        if (_reservedCoinByOrder.TryGetValue(orderId, out var coin) && coin > 0m)
        {
            _reservedCoinByOrder.Remove(orderId);

            if (_orders.TryGetValue(orderId, out var o))
            {
                var cur = _coinReserved.TryGetValue(o.Symbol, out var r) ? r : 0m;
                _coinReserved[o.Symbol] = Math.Max(0m, cur - coin);
            }
        }

        _remainingQty.Remove(orderId);
    }

    private decimal ApplySlippage(decimal px, OrderSide side)
    {
        if (_slippageBps <= 0m) return px;
        var factor = 1m + (_slippageBps / 10_000m);
        return side == OrderSide.Buy ? px * factor : px / factor;
    }

    private void EnsureStarted()
    {
        if (!_started) throw new InvalidOperationException($"{Name} not started. Call StartAsync().");
    }
}