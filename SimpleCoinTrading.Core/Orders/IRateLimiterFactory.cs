using System.Collections.Concurrent;
using SimpleCoinTrading.Core.Time.Clocks;

namespace SimpleCoinTrading.Core.Orders;

public interface IRateLimiterFactory
{
    IRateLimiter GetFor(string algorithmId);
}


public sealed class PerAlgorithmRateLimiterFactory : IRateLimiterFactory
{
    private readonly ConcurrentDictionary<string, IRateLimiter> _map = new();
    private readonly IClock _clock;
    private readonly int _maxPerSecond;

    public PerAlgorithmRateLimiterFactory(IClock clock, int maxPerSecond)
    {
        _clock = clock;
        _maxPerSecond = maxPerSecond;
    }

    public IRateLimiter GetFor(string algorithmId)
    {
        var key = string.IsNullOrWhiteSpace(algorithmId) ? "UNKNOWN" : algorithmId;

        return _map.GetOrAdd(key, id =>
            new PerSecondFixedWindowRateLimiter(_clock, _maxPerSecond, $"OrderPlace:{id}"));
    }
}
