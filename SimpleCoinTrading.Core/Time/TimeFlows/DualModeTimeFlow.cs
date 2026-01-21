using System.Diagnostics;
using System.Threading.Channels;
using SimpleCoinTrading.Core.Time.Clocks;

namespace SimpleCoinTrading.Core.Time.TimeFlows;
public enum TimeFlowMode
{
    Backtest,      // AdvanceTo로 점프(시장 이벤트 기반)
    RealTimeReplay // 시장시간까지 “실시간 속도”로 따라감
}


public sealed class DualModeTimeFlow : ITimeFlow, ITimeAdvancer
{
    private readonly TimeSpan _step;
    private readonly Channel<TimeTick> _ch;

    private readonly object _gate = new();

    private CancellationToken _ct;

    private DateTime? _lastMarketTime;   // 최근 받은 시장 시간(상한 cap)
    private bool _initialized;           // 최초 Tick 발행 여부

    private readonly VirtualClock _virtualClock;
    private TimeFlowMode _mode;

    // 실시간 리플레이 관련
    private readonly TimeSpan _poll;     // 실시간 모드에서 얼마나 자주 “전진”할지
    private Stopwatch? _sw;
    private DateTime? _replayAnchorVirtual; // 실시간 모드 시작 시 가상시간
    private TimeSpan _replaySpeed = TimeSpan.FromSeconds(1); // 1x (확장 가능)

    public ChannelReader<TimeTick> Ticks => _ch.Reader;

    public DualModeTimeFlow(
        VirtualClock virtualClock,
        TimeFlowMode mode,
        TimeSpan? step = null,
        TimeSpan? poll = null,
        int capacity = 8192)
    {
        _virtualClock = virtualClock;
        _mode = mode;
        _step = step ?? TimeSpan.FromSeconds(1);
        _poll = poll ?? TimeSpan.FromMilliseconds(100);

        _ch = Channel.CreateBounded<TimeTick>(new BoundedChannelOptions(capacity)
        {
            SingleWriter = false,
            SingleReader = false,
            FullMode = BoundedChannelFullMode.DropOldest
        });
    }

    public void Start(CancellationToken ct)
    {
        _ct = ct;
        ct.Register(() => _ch.Writer.TryComplete());

        if (_mode == TimeFlowMode.RealTimeReplay)
        {
            StartRealTimeLoop(ct);
        }
    }

    /// <summary>시장 이벤트 시간(UTC)으로 상한을 올리고(필수), Backtest 모드에서는 즉시 진행</summary>
    public void AdvanceTo(DateTime marketUtc)
    {
        marketUtc = EnsureUtc(marketUtc);
        if (_ct.IsCancellationRequested) return;

        lock (_gate)
        {
            // 상한 갱신
            if (_lastMarketTime is null || marketUtc > _lastMarketTime.Value)
                _lastMarketTime = marketUtc;

            // 최초 기준 잡기
            if (!_initialized)
            {
                _virtualClock.SetUtc(marketUtc);
                _ch.Writer.TryWrite(new TimeTick(marketUtc));
                _initialized = true;
                if (_mode == TimeFlowMode.RealTimeReplay)
                    EnsureReplayAnchors_NoLock(); // 실시간 모드 앵커 준비
                return;
            }

            // Backtest 모드: marketUtc까지 “즉시” tick 발행
            if (_mode == TimeFlowMode.Backtest)
            {
                EmitTicksUpTo_NoLock(marketUtc);
            }
            else
            {
                // RealTimeReplay 모드: 여기서는 상한만 올려두고,
                // 실제 전진/발행은 background loop가 한다.
                EnsureReplayAnchors_NoLock();
            }
        }
    }

    /// <summary>모드 변경(원하면 런타임에 전환 가능). 초보면 실행 전에만 설정 추천.</summary>
    public void SetMode(TimeFlowMode mode)
    {
        lock (_gate) _mode = mode;
    }

    private void StartRealTimeLoop(CancellationToken ct)
    {
        _sw = Stopwatch.StartNew();

        _ = Task.Run(async () =>
        {
            try
            {
                while (!ct.IsCancellationRequested)
                {
                    DateTime? cap;
                    DateTime? target;

                    lock (_gate)
                    {
                        cap = _lastMarketTime;
                        if (cap is null || !_initialized)
                        {
                            // 시장 시간이 아직 안 들어옴 → 기다림
                            target = null;
                        }
                        else
                        {
                            EnsureReplayAnchors_NoLock();

                            // 앵커 기준으로 “실제 경과시간만큼” 가상시간을 전진
                            // (1x 속도. 나중에 speed 배수 가능)
                            var elapsed = _sw!.Elapsed;
                            var desired = _replayAnchorVirtual!.Value + elapsed; // 1x
                            target = desired <= cap.Value ? desired : cap.Value; // 상한 cap
                        }
                    }

                    if (target.HasValue)
                    {
                        lock (_gate)
                        {
                            // 이미 그 이상 전진했으면 스킵
                            if (target.Value > _virtualClock.UtcNow)
                            {
                                EmitTicksUpTo_NoLock(target.Value);
                            }
                        }
                    }

                    await Task.Delay(_poll, ct);
                }
            }
            catch (OperationCanceledException) { }
        }, ct);
    }

    private void EnsureReplayAnchors_NoLock()
    {
        if (_sw is null) _sw = Stopwatch.StartNew();
        if (_replayAnchorVirtual is null && _initialized)
        {
            _replayAnchorVirtual = _virtualClock.UtcNow;
            _sw.Restart(); // 앵커 시점부터 elapsed를 다시 측정
        }
    }

    private void EmitTicksUpTo_NoLock(DateTime targetUtc)
    {
        var current = _virtualClock.UtcNow;
        
        if (!_initialized) return;

        var t = current;
        var next = AlignNext(t, _step);

        while (next <= targetUtc)
        {
            EmitTick(next);
            next = next.Add(_step);
        }
        
        // Ensure virtual clock reaches the target time even if no ticks were emitted or after the last tick
        _virtualClock.AdvanceToUtc(targetUtc);
    }

    private void EmitTick(DateTime utc)
    {
        _virtualClock.AdvanceToUtc(utc);
        _ch.Writer.TryWrite(new TimeTick(utc));
    }

    private static DateTime EnsureUtc(DateTime dt)
        => dt.Kind == DateTimeKind.Utc ? dt : DateTime.SpecifyKind(dt, DateTimeKind.Utc);

    private static DateTime AlignNext(DateTime t, TimeSpan step)
    {
        var ticks = step.Ticks;
        var nextTicks = ((t.Ticks / ticks) + 1) * ticks;
        return new DateTime(nextTicks, DateTimeKind.Utc);
    }
}
