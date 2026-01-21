using System.Collections.Concurrent;

namespace SimpleCoinTrading.Core.Orders;

public interface IOrderIdMap
{
    void Set(string clientOrderId, string orderId);

    bool TryGetOrderId(string clientOrderId, out string orderId);
}

public sealed class InMemoryOrderIdMap : IOrderIdMap
{
    private readonly ConcurrentDictionary<string, string> _map = new();

    public void Set(string clientOrderId, string orderId)
        => _map[clientOrderId] = orderId;

    public bool TryGetOrderId(string clientOrderId, out string orderId)
        => _map.TryGetValue(clientOrderId, out orderId!);
}