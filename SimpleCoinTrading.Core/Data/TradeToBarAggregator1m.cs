using SimpleCoinTrading.Core.Time;
using SimpleCoinTrading.Core.Time.TimeFlows;

namespace SimpleCoinTrading.Core.Data;

public sealed class TradeToBarAggregator1m
{
    private readonly Action<BarClosedEvent> _onBarClosed;
    private readonly Dictionary<string, BarBuilder> _state = new(StringComparer.OrdinalIgnoreCase);

    public TradeToBarAggregator1m(Action<BarClosedEvent> onBarClosed)
    {
        _onBarClosed = onBarClosed ?? throw new ArgumentNullException(nameof(onBarClosed));
    }

    // 핵심: trade가 들어올 때마다 호출
    public void OnTrade(string symbol, in TradeTick tick)
    {
        if (string.IsNullOrWhiteSpace(symbol)) return;

        var bucket = FloorToMinute(tick.TimeUtc);

        if (!_state.TryGetValue(symbol, out var b))
        {
            b = new BarBuilder(bucket);
            _state[symbol] = b;
        }

        // 분이 바뀐 경우: 이전 bar를 close하고 새 builder 시작
        if (bucket > b.BucketTimeUtc)
        {
            // tick이 점프해서 여러 분이 비었을 수도 있음
            // 여기서는 "이전 분만 close"하고, 빈 분은 생성하지 않음(옵션에서 확장 가능)
            CloseAndPublish(symbol, ref b);

            // 새 분 버킷 시작
            b = new BarBuilder(bucket);
            _state[symbol] = b;
        }
        else if (bucket < b.BucketTimeUtc)
        {
            // out-of-order trade(과거 틱) 무시(운영에서는 별도 처리할 수도)
            return;
        }

        // 현재 분에 tick 반영
        b.Add(tick);
        _state[symbol] = b;
    }

    // 분 경계 처리 (마지막 거래가 있는 분의 종가를 사용하여 빈 분을 채울 수도 있음)
    // 현재 구현은 데이터가 없는 분은 Bar를 생성하지 않음.
    // 하지만 "현재 진행 중인 분"이 끝났는데 데이터가 안 들어오면 강제로 닫을 때 사용.
    public void FlushIfMinutePassed(string symbol, DateTime nowUtc)
    {
        if (!_state.TryGetValue(symbol, out var b)) return;

        var currentBucket = FloorToMinute(nowUtc);

        // now가 builder 분을 이미 넘어갔다면 닫는다
        if (currentBucket > b.BucketTimeUtc)
        {
            CloseAndPublish(symbol, ref b);
            _state.Remove(symbol);
        }
    }

    // 전체 심볼 강제 flush
    public void FlushAll(DateTime nowUtc)
    {
        var symbols = new List<string>(_state.Keys);
        foreach (var sym in symbols)
            FlushIfMinutePassed(sym, nowUtc);
    }

    private void CloseAndPublish(string symbol, ref BarBuilder b)
    {
        if (!b.HasAnyTrade)
            return;

        // 1분봉 시간은 버킷 시작 시각으로 둠
        var barTime = b.BucketTimeUtc;
        var bar = b.Build();

        _onBarClosed(new BarClosedEvent(symbol, Resolution.M1, barTime, bar));
    }

    private static DateTime FloorToMinute(DateTime utc)
    {
        if (utc.Kind != DateTimeKind.Utc)
            utc = DateTime.SpecifyKind(utc, DateTimeKind.Utc);

        return new DateTime(utc.Year, utc.Month, utc.Day, utc.Hour, utc.Minute, 0, DateTimeKind.Utc);
    }

    // 내부 builder (값타입으로 두면 Dictionary에서 다루기 까다로워서 class로 둠)
    private sealed class BarBuilder
    {
        public DateTime BucketTimeUtc { get; private set; }

        public bool HasAnyTrade { get; private set; }
        private decimal _open;
        private decimal _high;
        private decimal _low;
        private decimal _close;
        private decimal _volume;

        public BarBuilder(DateTime bucketTimeUtc)
        {
            BucketTimeUtc = bucketTimeUtc;
            HasAnyTrade = false;
            _open = _high = _low = _close = 0m;
            _volume = 0m;
        }

        public void Add(in TradeTick t)
        {
            if (!HasAnyTrade)
            {
                _open = _high = _low = _close = t.Price;
                _volume = t.Quantity;
                HasAnyTrade = true;
                return;
            }

            if (t.Price > _high) _high = t.Price;
            if (t.Price < _low) _low = t.Price;
            _close = t.Price;
            _volume += t.Quantity;
        }

        public Bar Build()
        {
            return new Bar(
                TimeUtc: BucketTimeUtc,
                Open: _open,
                High: _high,
                Low: _low,
                Close: _close,
                Volume: _volume
            );
        }
    }
}