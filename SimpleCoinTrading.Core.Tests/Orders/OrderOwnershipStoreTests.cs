using SimpleCoinTrading.Core.Orders;
using Xunit;

namespace SimpleCoinTrading.Core.Tests.Orders;

public class OrderOwnershipStoreTests
{
    [Fact]
    public void SetOwner_Should_StoreMapping()
    {
        // Arrange
        var store = new InMemoryOrderOwnershipStore();
        var orderId = "order-123";
        var algoId = "algo-abc";

        // Act
        store.SetOwner(orderId, algoId);

        // Assert
        var found = store.TryGetOwner(orderId, out var actualAlgoId);
        Assert.True(found);
        Assert.Equal(algoId, actualAlgoId);
    }

    [Fact]
    public void GetOrderIds_Should_ReturnAllOrdersForAlgorithm()
    {
        // Arrange
        var store = new InMemoryOrderOwnershipStore();
        store.SetOwner("o1", "a1");
        store.SetOwner("o2", "a1");
        store.SetOwner("o3", "a2");

        // Act
        var a1Orders = store.GetOrderIds("a1");

        // Assert
        Assert.Equal(2, a1Orders.Count);
        Assert.Contains("o1", a1Orders);
        Assert.Contains("o2", a1Orders);
    }

    [Fact]
    public void SetOwner_Should_NormalizeAlgorithmId()
    {
        // Arrange
        var store = new InMemoryOrderOwnershipStore();
        
        // Act
        store.SetOwner("o1", null!);
        store.SetOwner("o2", "  ");

        // Assert
        store.TryGetOwner("o1", out var a1);
        store.TryGetOwner("o2", out var a2);
        Assert.Equal("UNKNOWN", a1);
        Assert.Equal("UNKNOWN", a2);
    }

    [Fact]
    public void Should_Be_CaseInsensitive()
    {
        // Arrange
        var store = new InMemoryOrderOwnershipStore();
        store.SetOwner("ORDER-1", "ALGO-1");

        // Act & Assert
        Assert.True(store.TryGetOwner("order-1", out var algo));
        Assert.Equal("ALGO-1", algo);
        
        var orders = store.GetOrderIds("algo-1");
        Assert.Single(orders);
        Assert.Equal("ORDER-1", orders.First());
    }
    [Fact]
    public void Remove_Should_CleanUpMapping()
    {
        // Arrange
        var store = new InMemoryOrderOwnershipStore();
        store.SetOwner("o1", "a1");

        // Act
        store.Remove("o1", "a1");

        // Assert
        Assert.False(store.TryGetOwner("o1", out _));
        Assert.Empty(store.GetOrderIds("a1"));
    }
}
