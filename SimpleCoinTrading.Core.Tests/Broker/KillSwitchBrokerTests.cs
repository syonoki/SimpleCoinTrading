using FluentAssertions;
using NSubstitute;
using SimpleCoinTrading.Core.Broker;
using SimpleCoinTrading.Core.Orders;

namespace SimpleCoinTrading.Core.Tests.Broker;

public class KillSwitchBrokerTests
{
    private readonly IBroker _inner = Substitute.For<IBroker>();
    private readonly OrderStateProjection _state = new();
    private readonly KillSwitchBroker _sut;

    public KillSwitchBrokerTests()
    {
        _sut = new KillSwitchBroker(_inner, _state);
    }

    [Fact]
    public async Task PlaceOrderAsync_WhenKillSwitchOff_ShouldDelegateToInner()
    {
        // Arrange
        var request = new PlaceOrderRequest("BTC", OrderSide.Buy, OrderType.Limit, 1.0m, 50000000m);
        var expectedAck = new OrderAck(true, "order-1", "client-1");
        _inner.PlaceOrderAsync(request, Arg.Any<CancellationToken>()).Returns(expectedAck);

        // Act
        var result = await _sut.PlaceOrderAsync(request, default);

        // Assert
        result.Should().Be(expectedAck);
        await _inner.Received(1).PlaceOrderAsync(request, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task PlaceOrderAsync_WhenKillSwitchOn_ShouldThrowException()
    {
        // Arrange
        _state.SetKillSwitch(true);
        var request = new PlaceOrderRequest("BTC", OrderSide.Buy, OrderType.Limit, 1.0m, 50000000m);

        // Act
        var action = () => _sut.PlaceOrderAsync(request, default);

        // Assert
        await action.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*KillSwitch is ON*");
        await _inner.DidNotReceiveWithAnyArgs().PlaceOrderAsync(default!, default);
    }
}
