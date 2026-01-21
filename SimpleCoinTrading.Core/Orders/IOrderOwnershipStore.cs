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
    private readonly ConcurrentDictionary<string, string> _orderToAlgo = new();
    private readonly ConcurrentDictionary<string, ConcurrentDictionary<string, byte>> _algoToOrders = new();

    public void SetOwner(string orderId, string algorithmId)
    {
        _orderToAlgo[orderId] = algorithmId;

        var set = _algoToOrders.GetOrAdd(algorithmId, _ => new ConcurrentDictionary<string, byte>());
        set[orderId] = 0;
    }

    public bool TryGetOwner(string orderId, out string algorithmId)
        => _orderToAlgo.TryGetValue(orderId, out algorithmId!);

    public IReadOnlyCollection<string> GetOrderIds(string algorithmId)
    {
        if (_algoToOrders.TryGetValue(algorithmId, out var set))
            return set.Keys.ToArray();

        return Array.Empty<string>();
    }

    public void Remove(string orderId, string algorithmId)
    {
        _orderToAlgo.TryRemove(orderId, out _);

        if (_algoToOrders.TryGetValue(algorithmId, out var set))
            set.TryRemove(orderId, out _);
    }
}
