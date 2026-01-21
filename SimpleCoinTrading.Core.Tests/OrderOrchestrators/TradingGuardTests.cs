using FluentAssertions;
using SimpleCoinTrading.Core.Orders;

namespace SimpleCoinTrading.Core.Tests.OrderOrchestrators;

public class TradingGuardTests
{
    [Fact]
    public void InitialState_ShouldNotBeReadOnly()
    {
        // Arrange
        var guard = new TradingGuard();

        // Assert
        guard.IsReadOnly.Should().BeFalse();
        guard.Reason.Should().BeNull();
    }

    [Fact]
    public void Trip_ShouldSetReadOnlyAndReason()
    {
        // Arrange
        var guard = new TradingGuard();
        const string reason = "Too many errors";

        // Act
        guard.Trip(reason);

        // Assert
        guard.IsReadOnly.Should().BeTrue();
        guard.Reason.Should().Be(reason);
    }

    [Fact]
    public void Clear_ShouldResetReadOnlyAndReason()
    {
        // Arrange
        var guard = new TradingGuard();
        guard.Trip("Something happened");

        // Act
        guard.Clear();

        // Assert
        guard.IsReadOnly.Should().BeFalse();
        guard.Reason.Should().BeNull();
    }
}
