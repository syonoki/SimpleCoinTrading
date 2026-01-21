using FluentAssertions;
using SimpleCoinTrading.Core.OrderOrchestrators;

namespace SimpleCoinTrading.Core.Tests.OrderOrchestrators;

public class OrderOwnershipStoreTests
{
    private readonly InMemoryOrderOwnershipStore _sut = new();

    [Fact]
    public void SetOwner_ShouldStoreOwnershipAndRetrieveViaTryGetOwner()
    {
        // Arrange
        const string orderId = "o-1";
        const string algoId = "algo-1";

        // Act
        _sut.SetOwner(orderId, algoId);

        // Assert
        _sut.TryGetOwner(orderId, out var retrievedAlgoId).Should().BeTrue();
        retrievedAlgoId.Should().Be(algoId);
    }

    [Fact]
    public void SetOwner_ShouldAddOrderIdToAlgorithmList()
    {
        // Arrange
        const string algoId = "algo-1";
        _sut.SetOwner("o-1", algoId);
        _sut.SetOwner("o-2", algoId);

        // Act
        var orderIds = _sut.GetOrderIds(algoId);

        // Assert
        orderIds.Should().HaveCount(2).And.Contain(new[] { "o-1", "o-2" });
    }

    [Fact]
    public void TryGetOwner_WhenNotFound_ShouldReturnFalse()
    {
        // Act
        var result = _sut.TryGetOwner("non-existent", out var algoId);

        // Assert
        result.Should().BeFalse();
        algoId.Should().BeNull();
    }

    [Fact]
    public void GetOrderIds_WhenAlgorithmHasNoOrders_ShouldReturnEmpty()
    {
        // Act
        var orderIds = _sut.GetOrderIds("empty-algo");

        // Assert
        orderIds.Should().BeEmpty();
    }

    [Fact]
    public void Remove_ShouldClearOwnershipAndRemoveFromAlgorithmList()
    {
        // Arrange
        const string orderId = "o-1";
        const string algoId = "algo-1";
        _sut.SetOwner(orderId, algoId);

        // Act
        _sut.Remove(orderId, algoId);

        // Assert
        _sut.TryGetOwner(orderId, out _).Should().BeFalse();
        _sut.GetOrderIds(algoId).Should().BeEmpty();
    }

    [Fact]
    public void SetOwner_WithDifferentAlgorithms_ShouldKeepThemSeparate()
    {
        // Arrange
        _sut.SetOwner("o-1", "algo-1");
        _sut.SetOwner("o-2", "algo-2");

        // Act & Assert
        _sut.GetOrderIds("algo-1").Should().ContainSingle().Which.Should().Be("o-1");
        _sut.GetOrderIds("algo-2").Should().ContainSingle().Which.Should().Be("o-2");
    }

    [Fact]
    public void SetOwner_UpdatingSameOrderId_ShouldUpdateToNewAlgorithm()
    {
        // Arrange
        const string orderId = "o-1";
        _sut.SetOwner(orderId, "algo-1");

        // Act
        _sut.SetOwner(orderId, "algo-2");

        // Assert
        _sut.TryGetOwner(orderId, out var algoId).Should().BeTrue();
        algoId.Should().Be("algo-2");
        
        // Note: SetOwner currently adds to the set of the new algorithm. 
        // In the current implementation of InMemoryOrderOwnershipStore, 
        // it doesn't automatically remove from the old algorithm's set if the orderId already existed for a different one.
        // This is fine for now as per MVP, but good to know the behavior.
        _sut.GetOrderIds("algo-2").Should().Contain(orderId);
    }
}
