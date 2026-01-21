using Microsoft.Extensions.Logging;
using SimpleCoinTrading.Core.Broker;
using SimpleCoinTrading.Core.Logs;
using SimpleCoinTrading.Core.Time.Clocks;

namespace SimpleCoinTrading.Core.OrderOrchestrators;

public sealed class DelegatingOrchestrator : IOrderOrchestrator
{
    private readonly IClock _clock;
    private readonly IBroker _broker;
    private readonly ITradingGuard _guard;
    private readonly IRateLimiterFactory _rateLimiterFactory;
    private readonly IIdempotencyStore _idempotencyStore;
    private readonly IOrderIdMap _orderIdMap;
    private readonly ILogger<DelegatingOrchestrator> _logger;
    private readonly IAlgorithmLogHub _algoLogHub;
    private readonly IOrderOwnershipStore _ownership;

    public DelegatingOrchestrator(IClock clock, IBroker broker, ITradingGuard guard,
        IRateLimiterFactory rateLimiterFactory,
        IIdempotencyStore idempotencyStore, IOrderIdMap orderIdMap, IOrderOwnershipStore ownership,
        ILogger<DelegatingOrchestrator> logger, 
        IAlgorithmLogHub algoLogHub)
    {
        _clock = clock;
        _broker = broker;
        _guard = guard;
        _rateLimiterFactory = rateLimiterFactory;
        _idempotencyStore = idempotencyStore;
        _orderIdMap = orderIdMap;
        _ownership = ownership;
        _logger = logger;
        _algoLogHub = algoLogHub;
    }

    public Task<OrderState?> GetOrderAsync(string orderId, CancellationToken ct = default)
    {
        return _broker.GetOrderAsync(orderId, ct);
    }

    public async Task<OrderAck> PlaceOrderAsync(PlaceOrderRequest request, CancellationToken ct = default)
    {
        var algoId = request.AlgorithmId ?? "UNKNOWN";

        _algoLogHub.Write(new AlgoLogEvent(_clock.UtcNow, algoId, AlgoLogLevel.Info,
            "Order requested algo={AlgoId} symbol={Symbol} clientOrderId={ClientOrderId}",
            request.Symbol, request.ClientOrderId));

        // 1) ReadOnly
        if (_guard.IsReadOnly)
        {
            _algoLogHub.Write(new AlgoLogEvent(DateTimeOffset.UtcNow, algoId, AlgoLogLevel.Warn,
                $"ORDER_BLOCKED reason=ReadOnly ({_guard.Reason})", Symbol: request.Symbol, ClientOrderId: 
                request.ClientOrderId));
            throw new InvalidOperationException($"ReadOnly: {_guard.Reason ?? "enabled"}");
        }
             

        // 2) ClientOrderId 보장 (없으면 생성)
        var clientOrderId = request.ClientOrderId;
        if (string.IsNullOrWhiteSpace(clientOrderId))
        {
            clientOrderId = $"AUTO:{DateTimeOffset.UtcNow:yyyyMMddHHmmssfff}:{Guid.NewGuid():N}";
            request = request with { ClientOrderId = clientOrderId };
        }

        // 3) Idempotency
        if (!_idempotencyStore.TryRegister(clientOrderId))
        {
            throw new InvalidOperationException($"Duplicate ClientOrderId: {clientOrderId}");
        }

        // 4) Rate limit
        var limiter = _rateLimiterFactory.GetFor(algoId);
        if (!limiter.TryConsume())
        {
            _algoLogHub.Write(new AlgoLogEvent(DateTimeOffset.UtcNow, algoId, AlgoLogLevel.Warn,
                $"ORDER_BLOCKED reason=RateLimit limiter={limiter.Name}", Symbol: request.Symbol, ClientOrderId: clientOrderId));
            _guard.Trip($"Rate limit exceeded ({limiter.Name})");
            throw new InvalidOperationException("Rate limit exceeded. Trading halted (ReadOnly).");
        }

        // 5) 실제 브로커 호출
        var result = await _broker.PlaceOrderAsync(request, ct);
        _algoLogHub.Write(new AlgoLogEvent(DateTimeOffset.UtcNow, algoId, AlgoLogLevel.Info,
            $"ORDER_SENT orderId={result.OrderId}", Symbol: request.Symbol, ClientOrderId: clientOrderId, OrderId: result.OrderId));
        
        _orderIdMap.Set(clientOrderId, result.OrderId);
        _ownership.SetOwner(result.OrderId, algoId);

        return result;
    }

    public Task CancelByClientOrderIdAsync(string clientOrderId, CancellationToken ct = default)
    {
        if (!_orderIdMap.TryGetOrderId(clientOrderId, out var orderId))
            throw new InvalidOperationException($"Unknown ClientOrderId: {clientOrderId}");

        return _broker.CancelOrderAsync(new CancelOrderRequest(orderId), ct);
    }

    public Task CancelAsync(string orderId, CancellationToken ct = default)
        => _broker.CancelOrderAsync(new CancelOrderRequest(orderId), ct);

    public Task CancelAllAsync(CancellationToken ct = default)
        => _broker.CancelAllAsync(ct);

    public async Task CancelAllByAlgorithmAsync(string algorithmId, CancellationToken ct = default)
    {
        var algoId = string.IsNullOrWhiteSpace(algorithmId) ? "UNKNOWN" : algorithmId;

        // 현재 알고리즘이 낸 주문들 스냅샷
        var orderIds = _ownership.GetOrderIds(algoId);

        // MVP: 순차 취소(단순). 나중에 병렬/스로틀 가능
        foreach (var orderId in orderIds)
        {
            try
            {
                await _broker.CancelOrderAsync(new CancelOrderRequest(orderId), ct);
            }
            catch
            {
                // 실패는 로그만 남기고 계속 (MVP)
                _logger.LogError($"Failed to cancel order {orderId} by {algoId}");
            }
        }
    }
}