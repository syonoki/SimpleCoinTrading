using SimpleCoinTrading.Core.Time.Clocks;

namespace SimpleCoinTrading.Core.Orders;

public interface IRateLimiter
{
    /// <summary>
    /// 토큰을 1개 소비 시도. 허용되면 true.
    /// </summary>
    bool TryConsume(int tokens = 1);

    /// <summary>디버깅/표시용</summary>
    string Name { get; }
}

public sealed class PerSecondFixedWindowRateLimiter : IRateLimiter
{
    private readonly IClock _clock;
    private readonly int _maxPerSecond;
    private long _currentSecond;
    private int _countInSecond;

    public string Name { get; }

    public PerSecondFixedWindowRateLimiter(IClock clock, int maxPerSecond, string? name = null)
    {
        _clock = clock ?? throw new ArgumentNullException(nameof(clock));
        if (maxPerSecond <= 0) throw new ArgumentOutOfRangeException(nameof(maxPerSecond));
        _maxPerSecond = maxPerSecond;
        Name = name ?? $"FixedWindow({_maxPerSecond}/sec)";
    }

    public bool TryConsume(int tokens = 1)
    {
        if (tokens <= 0) return true; // 요청이 0이면 항상 허용
        if (tokens != 1) throw new NotSupportedException("MVP: tokens=1 only"); // 필요하면 확장

        var nowSec = new DateTimeOffset(_clock.UtcNow).ToUnixTimeSeconds();

        long currentSec = Interlocked.Read(ref _currentSecond);
        if (currentSec != nowSec)
        {
            if (Interlocked.CompareExchange(ref _currentSecond, nowSec, currentSec) == currentSec)
            {
                Interlocked.Exchange(ref _countInSecond, 0);
            }
        }

        return Interlocked.Increment(ref _countInSecond) <= _maxPerSecond;
    }
}