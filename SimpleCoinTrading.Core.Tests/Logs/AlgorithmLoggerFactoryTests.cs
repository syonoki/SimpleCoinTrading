using NSubstitute;
using SimpleCoinTrading.Core.Logs;
using Xunit;

namespace SimpleCoinTrading.Core.Tests.Logs;

public class AlgorithmLoggerFactoryTests
{
    [Fact]
    public void Create_ShouldReturnAlgorithmLoggerWithCorrectId()
    {
        // Arrange
        var hub = Substitute.For<IAlgorithmLogHub>();
        var factory = new AlgorithmLoggerFactory(hub);
        var algoId = "factory-test-algo";

        // Act
        var logger = factory.Create(algoId);

        // Assert
        Assert.NotNull(logger);
        Assert.IsType<AlgorithmLogger>(logger);
        
        // Indirectly verify by writing a log and checking the hub
        logger.Info("Test");
        hub.Received(1).Write(Arg.Is<AlgoLogEvent>(e => e.AlgorithmId == algoId));
    }
}
