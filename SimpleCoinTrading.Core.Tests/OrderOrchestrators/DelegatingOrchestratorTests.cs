using FluentAssertions;
using NSubstitute;
using Microsoft.Extensions.Logging.Abstractions;
using SimpleCoinTrading.Core.Broker;
using SimpleCoinTrading.Core.Logs;
using SimpleCoinTrading.Core.Orders;
using SimpleCoinTrading.Core.Time.Clocks;

namespace SimpleCoinTrading.Core.Tests.OrderOrchestrators;

public class DelegatingOrchestratorTests
{
    private readonly IBroker _broker = Substitute.For<IBroker>();
    private readonly ITradingGuard _guard = Substitute.For<ITradingGuard>();
    private readonly IRateLimiterFactory _rateLimiterFactory = Substitute.For<IRateLimiterFactory>();
    private readonly IRateLimiter _rateLimiter = Substitute.For<IRateLimiter>();
    private readonly IIdempotencyStore _idempotencyStore = Substitute.For<IIdempotencyStore>();
    private readonly IOrderIdMap _orderIdMap = Substitute.For<IOrderIdMap>();
    private readonly IOrderOwnershipStore _ownership = Substitute.For<IOrderOwnershipStore>();
    private readonly IClock _clock = Substitute.For<IClock>();
    private readonly IAlgorithmLogHub _algoLogHub = Substitute.For<IAlgorithmLogHub>();
    private readonly DelegatingOrchestrator _sut;

    public DelegatingOrchestratorTests()
    {
        _clock.UtcNow.Returns(new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc));
        _sut = new DelegatingOrchestrator(
            _clock, 
            _broker, 
            _guard, 
            _rateLimiterFactory, 
            _idempotencyStore, 
            _orderIdMap, 
            _ownership,
            NullLogger<DelegatingOrchestrator>.Instance,
            _algoLogHub);
    }

    [Fact]
    public async Task PlaceOrderAsync_WhenReadOnly_ShouldThrowException()
    {
        // Arrange
        _guard.IsReadOnly.Returns(true);
        _guard.Reason.Returns("Halted");
        var request = new PlaceOrderRequest("BTC", OrderSide.Buy, OrderType.Market, 1);

        // Act
        var action = () => _sut.PlaceOrderAsync(request);

        // Assert
        await action.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*ReadOnly: Halted*");
        
        await _broker.DidNotReceiveWithAnyArgs().PlaceOrderAsync(default!);
    }

    [Fact]
    public async Task PlaceOrderAsync_WhenDuplicateClientOrderId_ShouldThrowException()
    {
        // Arrange
        _guard.IsReadOnly.Returns(false);
        _idempotencyStore.TryRegister(Arg.Any<string>()).Returns(false);
        var request = new PlaceOrderRequest("BTC", OrderSide.Buy, OrderType.Market, 1, ClientOrderId: "dup-id");

        // Act
        var action = () => _sut.PlaceOrderAsync(request);

        // Assert
        await action.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*Duplicate ClientOrderId: dup-id*");

        await _broker.DidNotReceiveWithAnyArgs().PlaceOrderAsync(default!);
    }

    [Fact]
    public async Task PlaceOrderAsync_WhenRateLimitExceeded_ShouldTripGuardAndThrow()
    {
        // Arrange
        _guard.IsReadOnly.Returns(false);
        _idempotencyStore.TryRegister(Arg.Any<string>()).Returns(true);
        _rateLimiterFactory.GetFor(Arg.Any<string>()).Returns(_rateLimiter);
        _rateLimiter.TryConsume().Returns(false);
        _rateLimiter.Name.Returns("Limit10");
        var request = new PlaceOrderRequest("BTC", OrderSide.Buy, OrderType.Market, 1, AlgorithmId: "Algo1");

        // Act
        var action = () => _sut.PlaceOrderAsync(request);

        // Assert
        await action.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*Rate limit exceeded*");
        
        _rateLimiterFactory.Received(1).GetFor("Algo1");
        _guard.Received(1).Trip(Arg.Is<string>(s => s.Contains("Limit10")));
        await _broker.DidNotReceiveWithAnyArgs().PlaceOrderAsync(default!);
    }

    [Fact]
    public async Task PlaceOrderAsync_WhenAllowed_ShouldCallBrokerAndSetOrderIdMap()
    {
        // Arrange
        _guard.IsReadOnly.Returns(false);
        _idempotencyStore.TryRegister(Arg.Any<string>()).Returns(true);
        _rateLimiterFactory.GetFor(Arg.Any<string>()).Returns(_rateLimiter);
        _rateLimiter.TryConsume().Returns(true);
        var request = new PlaceOrderRequest("BTC", OrderSide.Buy, OrderType.Market, 1, ClientOrderId: "c-1", AlgorithmId: "Algo2");
        var expectedAck = new OrderAck(true, "order-1", "c-1");
        _broker.PlaceOrderAsync(Arg.Any<PlaceOrderRequest>(), Arg.Any<CancellationToken>()).Returns(expectedAck);

        // Act
        var result = await _sut.PlaceOrderAsync(request);

        // Assert
        result.Should().Be(expectedAck);
        _rateLimiterFactory.Received(1).GetFor("Algo2");
        await _broker.Received(1).PlaceOrderAsync(Arg.Any<PlaceOrderRequest>(), Arg.Any<CancellationToken>());
        _orderIdMap.Received(1).Set("c-1", "order-1");
        _ownership.Received(1).SetOwner("order-1", "Algo2");
    }

    [Fact]
    public async Task CancelAllByAlgorithmAsync_ShouldCancelAllOrdersOwnedByAlgorithm()
    {
        // Arrange
        const string algoId = "Algo3";
        var orderIds = new[] { "o-1", "o-2" };
        _ownership.GetOrderIds(algoId).Returns(orderIds);

        // Act
        await _sut.CancelAllByAlgorithmAsync(algoId);

        // Assert
        await _broker.Received(1).CancelOrderAsync(Arg.Is<CancelOrderRequest>(r => r.OrderId == "o-1"), Arg.Any<CancellationToken>());
        await _broker.Received(1).CancelOrderAsync(Arg.Is<CancelOrderRequest>(r => r.OrderId == "o-2"), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task CancelByClientOrderIdAsync_WhenFound_ShouldCallBroker()
    {
        // Arrange
        const string clientOrderId = "c-1";
        const string orderId = "o-1";
        _orderIdMap.TryGetOrderId(clientOrderId, out Arg.Any<string>()!).Returns(x =>
        {
            x[1] = orderId;
            return true;
        });

        // Act
        await _sut.CancelByClientOrderIdAsync(clientOrderId);

        // Assert
        await _broker.Received(1).CancelOrderAsync(Arg.Is<CancelOrderRequest>(r => r.OrderId == orderId), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task CancelByClientOrderIdAsync_WhenNotFound_ShouldThrowException()
    {
        // Arrange
        const string clientOrderId = "unknown";
        _orderIdMap.TryGetOrderId(clientOrderId, out Arg.Any<string>()!).Returns(false);

        // Act
        var action = () => _sut.CancelByClientOrderIdAsync(clientOrderId);

        // Assert
        await action.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*Unknown ClientOrderId*");
    }

    [Fact]
    public async Task GetOrderAsync_ShouldDelegateToBroker()
    {
        // Arrange
        const string orderId = "id-123";
        _broker.GetOrderAsync(orderId, Arg.Any<CancellationToken>()).Returns((OrderState?)null);

        // Act
        await _sut.GetOrderAsync(orderId);

        // Assert
        await _broker.Received(1).GetOrderAsync(orderId, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task CancelAsync_ShouldDelegateToBroker()
    {
        // Arrange
        const string orderId = "id-123";

        // Act
        await _sut.CancelAsync(orderId);

        // Assert
        await _broker.Received(1).CancelOrderAsync(Arg.Is<CancelOrderRequest>(r => r.OrderId == orderId), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task CancelAllAsync_ShouldDelegateToBroker()
    {
        // Act
        await _sut.CancelAllAsync();

        // Assert
        await _broker.Received(1).CancelAllAsync(Arg.Any<CancellationToken>());
    }
}
