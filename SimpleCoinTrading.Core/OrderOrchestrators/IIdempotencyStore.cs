using System.Collections.Concurrent;

namespace SimpleCoinTrading.Core.OrderOrchestrators;

public interface IIdempotencyStore
{
    /// <summary>
    /// 처음 보는 키면 등록하고 true,
    /// 이미 처리된 키면 false
    /// </summary>
    bool TryRegister(string key);
}

public sealed class InMemoryIdempotencyStore : IIdempotencyStore
{
    private readonly ConcurrentDictionary<string, byte> _seen = new();

    public bool TryRegister(string key)
    {
        // Add 성공 = 처음 봄
        return _seen.TryAdd(key, 0);
    }
}
