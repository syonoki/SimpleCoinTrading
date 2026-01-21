using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using SimpleCoinTrading.Core.Broker;
using SimpleCoinTrading.Core.Logs;

namespace SimpleCoinTrading.Core.Orders;

public sealed class OrderLifecycleTracker : IHostedService, IDisposable
{
    private readonly IBroker _broker;
    private readonly IOrderOwnershipStore _ownership;
    private readonly ITradingGuard _guard;
    private readonly ILogger<OrderLifecycleTracker> _logger;
    private readonly IAlgorithmLogHub _algoLogHub;

    private IDisposable? _sub;

    public OrderLifecycleTracker(
        IBroker broker,
        IOrderOwnershipStore ownership,
        ITradingGuard guard,
        ILogger<OrderLifecycleTracker> logger,
        IAlgorithmLogHub algoLogHub)
    {
        _broker = broker;
        _ownership = ownership;
        _guard = guard;
        _logger = logger;
        _algoLogHub = algoLogHub;
    }

    public Task StartAsync(CancellationToken cancellationToken)
    {
        // BrokerEvent 스트림 구독 (구현에 맞게 수정)
        _sub = _broker.Events.Subscribe(OnBrokerEvent, OnBrokerError);
        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken)
    {
        _sub?.Dispose();
        _sub = null;
        return Task.CompletedTask;
    }

    private void OnBrokerEvent(BrokerEvent e)
    {
        switch (e)
        {
            case FillEvent fe:
                // Fill은 부분일 수도 있으니, 여기서 무조건 Remove하지 말고
                // "전량 체결"을 알 수 있으면 그때 Remove.
                // MVP에서는 OrderUpdatedEvent에서 최종 상태로 정리하는 게 더 안전.
                _logger.LogInformation("Fill: orderId={OrderId} qty={Qty}", fe.Fill.OrderId, fe.Fill.Quantity);
                break;

            case OrderUpdatedEvent oe:
                HandleOrderUpdated(oe);
                break;

            case BrokerErrorEvent be:
                HandleBrokerError(be);
                break;

            default:
                _logger.LogDebug("BrokerEvent: {Type}", e.GetType().Name);
                break;
        }
    }

    private void HandleOrderUpdated(OrderUpdatedEvent oe)
    {
        // 여기서 terminal 상태면 ownership 정리
        // (아래 Status 판별은 네 이벤트 필드명에 맞게 수정)
        var orderId = oe.Order.OrderId;

        if (!_ownership.TryGetOwner(orderId, out var algoId))
        {
            // 소유권을 모르는 주문(수동/외부)일 수도 있음
            _logger.LogDebug("OrderUpdated but owner not found: {OrderId}", orderId);
            return;
        }

        if (IsTerminal(oe))
        {
            _algoLogHub.Write(new AlgoLogEvent(DateTimeOffset.UtcNow, algoId, AlgoLogLevel.Info,
                $"ORDER_TERMINAL status={oe.Order.Status} orderId={oe.Order.OrderId}",
                Symbol: oe.Order.Symbol, OrderId: oe.Order.OrderId));
            _ownership.Remove(orderId, algoId);
            _logger.LogInformation("Order terminal -> removed ownership. algo={AlgoId} orderId={OrderId} status={Status}",
                algoId, orderId, GetStatusText(oe));
        }
    }

    private static bool IsTerminal(OrderUpdatedEvent oe)
    {
        return oe.Order.Status is OrderStatus.Filled
            or OrderStatus.Canceled
            or OrderStatus.Rejected
            or OrderStatus.Expired;
    }

    private static string GetStatusText(OrderUpdatedEvent oe)
    {
        // return oe.Status.ToString();
        return "Unknown";
    }

    private void HandleBrokerError(BrokerErrorEvent be)
    {
        // 운영 정책: 브로커 레벨 에러가 특정 조건이면 ReadOnly 트립
        // (paper라도 트립하는 게 디버깅에 도움)
        _guard.Trip($"BrokerError: {be.Message}");
        _logger.LogError("BrokerError -> ReadOnly tripped. msg={Msg}", be.Message);
    }

    private void OnBrokerError(Exception ex)
    {
        _guard.Trip($"Broker stream error: {ex.Message}");
        _logger.LogError(ex, "Broker event stream error -> ReadOnly tripped.");
    }

    public void Dispose() => _sub?.Dispose();
}