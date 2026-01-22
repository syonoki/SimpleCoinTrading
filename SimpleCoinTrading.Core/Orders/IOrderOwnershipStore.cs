using System.Collections.Concurrent;

namespace SimpleCoinTrading.Core.Orders;

public interface IOrderOwnershipStore
{
    void SetOwner(string orderId, string algorithmId);
    bool TryGetOwner(string orderId, out string algorithmId);

    // 알고리즘이 가진 orderId 목록 조회
    IReadOnlyCollection<string> GetOrderIds(string algorithmId);

    // 취소 후 정리
    void Remove(string orderId, string algorithmId);
}

public sealed class InMemoryOrderOwnershipStore : IOrderOwnershipStore
{
    private readonly ConcurrentDictionary<string, string> _orderToAlgo = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, ConcurrentDictionary<string, byte>> _algoToOrders = new(StringComparer.OrdinalIgnoreCase);

    public void SetOwner(string orderId, string algorithmId)
    {
        var algo = Norm(algorithmId);
        _orderToAlgo[orderId] = algo;

        var set = _algoToOrders.GetOrAdd(algo, _ => new ConcurrentDictionary<string, byte>(StringComparer.OrdinalIgnoreCase));
        set[orderId] = 0;
    }

    public bool TryGetOwner(string orderId, out string algorithmId)
        => _orderToAlgo.TryGetValue(orderId, out algorithmId!);

    public IReadOnlyCollection<string> GetOrderIds(string algorithmId)
    {
        var algo = Norm(algorithmId);
        if (_algoToOrders.TryGetValue(algo, out var set))
            return set.Keys.ToArray();

        return Array.Empty<string>();
    }

    public void Remove(string orderId, string algorithmId)
    {
        _orderToAlgo.TryRemove(orderId, out _);

        var algo = Norm(algorithmId);
        if (_algoToOrders.TryGetValue(algo, out var set))
            set.TryRemove(orderId, out _);
    }

    private static string Norm(string s) => string.IsNullOrWhiteSpace(s) ? "UNKNOWN" : s;
}
