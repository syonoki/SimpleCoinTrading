using SimpleCoinTrading.Core.Broker;

namespace SimpleCoinTrading.Core.Orders;

public interface IOrderOrchestrator
{
    Task<OrderState?> GetOrderAsync(string orderId, CancellationToken ct = default);
    Task<OrderAck> PlaceOrderAsync(PlaceOrderRequest request, CancellationToken ct = default);
    Task CancelAsync(string orderId, CancellationToken ct = default);
    Task CancelAllAsync(CancellationToken ct = default); 
    
    Task CancelByClientOrderIdAsync(string clientOrderId, CancellationToken ct = default);
    Task CancelAllByAlgorithmAsync(string algorithmId, CancellationToken ct = default);
}