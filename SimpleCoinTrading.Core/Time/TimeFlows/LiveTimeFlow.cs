using System.Threading.Channels;
using SimpleCoinTrading.Core.Time.Clocks;

namespace SimpleCoinTrading.Core.Time.TimeFlows;

public sealed class LiveTimeFlow : ITimeFlow
{
    private readonly IClock _clock;
    private readonly TimeSpan _period;
    private readonly Channel<TimeTick> _ch;

    public ChannelReader<TimeTick> Ticks => _ch.Reader;

    public LiveTimeFlow(IClock clock, TimeSpan? period = null, int capacity = 1024)
    {
        _clock = clock;
        _period = period ?? TimeSpan.FromSeconds(1);

        // bounded: 느린 소비자 때문에 메모리 폭주 방지
        // DropOldest: 시간 tick은 "최신이 중요"하므로 오래된 tick은 버려도 안전
        _ch = Channel.CreateBounded<TimeTick>(new BoundedChannelOptions(capacity)
        {
            SingleWriter = true,
            SingleReader = false,
            FullMode = BoundedChannelFullMode.DropOldest
        });
    }

    public void Start(CancellationToken ct)
    {
        _ = Task.Run(async () =>
        {
            try
            {
                while (!ct.IsCancellationRequested)
                {
                    _ch.Writer.TryWrite(new TimeTick(_clock.UtcNow));
                    await Task.Delay(_period, ct);
                }
            }
            catch (OperationCanceledException) { }
            finally
            {
                _ch.Writer.TryComplete();
            }
        }, ct);
    }
}
