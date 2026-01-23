using SimpleCoinTrading.Core.Data;
using SimpleCoinTrading.Core.Utils;
using Xunit;

namespace SimpleCoinTrading.Core.Tests.Data;

public class RingBufferTests
{
    [Fact]
    public void Add_ShouldStoreElements()
    {
        // Arrange
        var buffer = new RingBuffer<int>(3);

        // Act
        buffer.Add(1);
        buffer.Add(2);

        // Assert
        Assert.Equal(2, buffer.LastOrDefault());
    }

    [Fact]
    public void LastOrDefault_ShouldReturnDefault_WhenEmpty()
    {
        // Arrange
        var buffer = new RingBuffer<int>(3);

        // Act & Assert
        Assert.Equal(0, buffer.LastOrDefault());
    }

    [Fact]
    public void Add_ShouldOverwriteOldElements_WhenCapacityExceeded()
    {
        // Arrange
        var buffer = new RingBuffer<int>(3);

        // Act
        buffer.Add(1);
        buffer.Add(2);
        buffer.Add(3);
        buffer.Add(4); // Overwrites 1

        // Assert
        Assert.Equal(4, buffer.LastOrDefault());
        
        var tail = buffer.Tail(3);
        Assert.Equal(new[] { 2, 3, 4 }, tail);
    }

    [Fact]
    public void Tail_ShouldReturnCorrectNumber_WhenRequestedSizeIsSmallerThanCount()
    {
        // Arrange
        var buffer = new RingBuffer<int>(5);
        for (int i = 1; i <= 5; i++) buffer.Add(i);

        // Act
        var tail = buffer.Tail(2);

        // Assert
        Assert.Equal(new[] { 4, 5 }, tail);
    }

    [Fact]
    public void Tail_ShouldReturnAllElements_WhenRequestedSizeIsLargerThanCount()
    {
        // Arrange
        var buffer = new RingBuffer<int>(5);
        buffer.Add(1);
        buffer.Add(2);

        // Act
        var tail = buffer.Tail(10);

        // Assert
        Assert.Equal(new[] { 1, 2 }, tail);
    }

    [Fact]
    public void Tail_ShouldReturnEmpty_WhenBufferIsEmpty()
    {
        // Arrange
        var buffer = new RingBuffer<int>(5);

        // Act
        var tail = buffer.Tail(3);

        // Assert
        Assert.Empty(tail);
    }

    [Fact]
    public void Tail_ShouldHandleWrapAroundCorrectly()
    {
        // Arrange
        var buffer = new RingBuffer<int>(3);
        buffer.Add(1);
        buffer.Add(2);
        buffer.Add(3);
        buffer.Add(4); // [4, 2, 3], head = 1, count = 3

        // Act
        var tail = buffer.Tail(3);

        // Assert
        Assert.Equal(new[] { 2, 3, 4 }, tail);
    }

    [Fact]
    public async Task ThreadSafety_AddAndTail_ShouldWorkCorrectly()
    {
        // Arrange
        var buffer = new RingBuffer<int>(100);
        int iterations = 1000;
        
        // Act
        var task1 = Task.Run(() => {
            for (int i = 0; i < iterations; i++)
            {
                buffer.Add(i);
            }
        });

        var task2 = Task.Run(() => {
            for (int i = 0; i < iterations; i++)
            {
                var tail = buffer.Tail(10);
                // We just want to ensure it doesn't throw and returns something reasonable
                Assert.NotNull(tail);
                Assert.True(tail.Count <= 10);
            }
        });

        await Task.WhenAll(task1, task2);

        // Assert
        // After all adds, the last added should be iterations - 1
        Assert.Equal(iterations - 1, buffer.LastOrDefault());
    }
}
