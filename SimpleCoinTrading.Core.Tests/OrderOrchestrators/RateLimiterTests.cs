using FluentAssertions;
using NSubstitute;
using SimpleCoinTrading.Core.Orders;
using SimpleCoinTrading.Core.Time.Clocks;

namespace SimpleCoinTrading.Core.Tests.OrderOrchestrators;

public class RateLimiterTests
{
    private readonly IClock _clock = Substitute.For<IClock>();

    [Fact]
    public void TryConsume_WithinLimit_ShouldReturnTrue()
    {
        // Arrange
        _clock.UtcNow.Returns(new DateTime(2026, 1, 1, 10, 0, 0, DateTimeKind.Utc));
        var limiter = new PerSecondFixedWindowRateLimiter(_clock, 5);

        // Act & Assert
        for (int i = 0; i < 5; i++)
        {
            limiter.TryConsume().Should().BeTrue();
        }
    }

    [Fact]
    public void TryConsume_ExceedLimit_ShouldReturnFalse()
    {
        // Arrange
        _clock.UtcNow.Returns(new DateTime(2026, 1, 1, 10, 0, 0, DateTimeKind.Utc));
        var limiter = new PerSecondFixedWindowRateLimiter(_clock, 2);

        // Act
        limiter.TryConsume().Should().BeTrue();
        limiter.TryConsume().Should().BeTrue();
        
        // Assert
        limiter.TryConsume().Should().BeFalse();
    }

    [Fact]
    public void TryConsume_AfterOneSecond_ShouldReset()
    {
        // Arrange
        var startTime = new DateTime(2026, 1, 1, 10, 0, 0, DateTimeKind.Utc);
        _clock.UtcNow.Returns(startTime);
        var limiter = new PerSecondFixedWindowRateLimiter(_clock, 1);

        // Act
        limiter.TryConsume().Should().BeTrue();
        limiter.TryConsume().Should().BeFalse();

        // Advance clock by 1 second
        _clock.UtcNow.Returns(startTime.AddSeconds(1));

        // Assert
        limiter.TryConsume().Should().BeTrue();
    }

    [Fact]
    public void Constructor_InvalidLimit_ShouldThrow()
    {
        // Act
        var action = () => new PerSecondFixedWindowRateLimiter(_clock, 0);

        // Assert
        action.Should().Throw<ArgumentOutOfRangeException>();
    }
}
