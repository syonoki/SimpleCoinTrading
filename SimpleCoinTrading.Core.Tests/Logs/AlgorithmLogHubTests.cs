using System.Threading.Channels;
using SimpleCoinTrading.Core.Logs;
using Xunit;

namespace SimpleCoinTrading.Core.Tests.Logs;

public class AlgorithmLogHubTests
{
    [Fact]
    public void Write_ShouldStoreAndRetrieveRecentLogs()
    {
        // Arrange
        var hub = new AlgorithmLogHub(capacityPerAlgo: 10);
        var algoId = "test-algo";
        var log1 = new AlgoLogEvent(DateTimeOffset.UtcNow, algoId, AlgoLogLevel.Info, "Message 1");
        var log2 = new AlgoLogEvent(DateTimeOffset.UtcNow, algoId, AlgoLogLevel.Info, "Message 2");

        // Act
        hub.Write(log1);
        hub.Write(log2);
        var recent = hub.GetRecent(algoId, 10);

        // Assert
        Assert.Equal(2, recent.Count);
        Assert.Equal("Message 1", recent[0].Message);
        Assert.Equal("Message 2", recent[1].Message);
    }

    [Fact]
    public void Write_ShouldHandleEmptyAlgorithmIdAsUnknown()
    {
        // Arrange
        var hub = new AlgorithmLogHub();
        var log = new AlgoLogEvent(DateTimeOffset.UtcNow, "", AlgoLogLevel.Info, "Unknown message");

        // Act
        hub.Write(log);
        var recent = hub.GetRecent("UNKNOWN", 10);
        var recentNull = hub.GetRecent(null!, 10);

        // Assert
        Assert.Single(recent);
        Assert.Equal("Unknown message", recent[0].Message);
        Assert.Single(recentNull);
        Assert.Equal("Unknown message", recentNull[0].Message);
    }

    [Fact]
    public async Task Subscribe_ShouldReceiveLogsInRealTime()
    {
        // Arrange
        var hub = new AlgorithmLogHub();
        var algoId = "stream-algo";
        var reader = hub.Subscribe(algoId);
        var log = new AlgoLogEvent(DateTimeOffset.UtcNow, algoId, AlgoLogLevel.Debug, "Live log");

        // Act
        hub.Write(log);

        // Assert
        var cts = new CancellationTokenSource(TimeSpan.FromSeconds(1));
        Assert.True(await reader.WaitToReadAsync(cts.Token));
        Assert.True(reader.TryRead(out var received));
        Assert.Equal("Live log", received.Message);
    }

    [Fact]
    public async Task Subscribe_ShouldWorkForMultipleSubscribers()
    {
        // Arrange
        var hub = new AlgorithmLogHub();
        var algoId = "multi-sub-algo";
        var reader1 = hub.Subscribe(algoId);
        var reader2 = hub.Subscribe(algoId);
        var log = new AlgoLogEvent(DateTimeOffset.UtcNow, algoId, AlgoLogLevel.Warn, "Broadcast log");

        // Act
        hub.Write(log);

        // Assert
        var cts = new CancellationTokenSource(TimeSpan.FromSeconds(1));
        
        Assert.True(await reader1.WaitToReadAsync(cts.Token));
        Assert.True(reader1.TryRead(out var received1));
        Assert.Equal("Broadcast log", received1.Message);

        Assert.True(await reader2.WaitToReadAsync(cts.Token));
        Assert.True(reader2.TryRead(out var received2));
        Assert.Equal("Broadcast log", received2.Message);
    }

    [Fact]
    public void GetRecent_ShouldRespectLimit()
    {
        // Arrange
        var hub = new AlgorithmLogHub(capacityPerAlgo: 100);
        var algoId = "limit-algo";
        for (int i = 1; i <= 20; i++)
        {
            hub.Write(new AlgoLogEvent(DateTimeOffset.UtcNow, algoId, AlgoLogLevel.Info, $"Msg {i}"));
        }

        // Act
        var recent = hub.GetRecent(algoId, 5);

        // Assert
        Assert.Equal(5, recent.Count);
        Assert.Equal("Msg 16", recent[0].Message);
        Assert.Equal("Msg 20", recent[4].Message);
    }

    [Fact]
    public void GetRecent_ShouldRespectCapacity()
    {
        // Arrange
        var hub = new AlgorithmLogHub(capacityPerAlgo: 5);
        var algoId = "capacity-algo";
        for (int i = 1; i <= 10; i++)
        {
            hub.Write(new AlgoLogEvent(DateTimeOffset.UtcNow, algoId, AlgoLogLevel.Info, $"Msg {i}"));
        }

        // Act
        var recent = hub.GetRecent(algoId, 10);

        // Assert
        Assert.Equal(5, recent.Count);
        Assert.Equal("Msg 6", recent[0].Message);
        Assert.Equal("Msg 10", recent[4].Message);
    }
}
