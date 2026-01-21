using NSubstitute;
using SimpleCoinTrading.Core.Logs;
using Xunit;

namespace SimpleCoinTrading.Core.Tests.Logs;

public class AlgorithmLoggerTests
{
    private readonly IAlgorithmLogHub _hub = Substitute.For<IAlgorithmLogHub>();
    private readonly string _algoId = "test-algo";
    private readonly AlgorithmLogger _logger;

    public AlgorithmLoggerTests()
    {
        _logger = new AlgorithmLogger(_algoId, _hub);
    }

    [Fact]
    public void Info_ShouldWriteToHub()
    {
        // Act
        _logger.Info("Hello Info", "BTC");

        // Assert
        _hub.Received(1).Write(Arg.Is<AlgoLogEvent>(e =>
            e.AlgorithmId == _algoId &&
            e.Level == AlgoLogLevel.Info &&
            e.Message == "Hello Info" &&
            e.Symbol == "BTC"
        ));
    }

    [Fact]
    public void Warn_ShouldWriteToHub()
    {
        // Act
        _logger.Warn("Hello Warn");

        // Assert
        _hub.Received(1).Write(Arg.Is<AlgoLogEvent>(e =>
            e.AlgorithmId == _algoId &&
            e.Level == AlgoLogLevel.Warn &&
            e.Message == "Hello Warn"
        ));
    }

    [Fact]
    public void Error_WithException_ShouldIncludeExceptionMessage()
    {
        // Arrange
        var ex = new Exception("Test Exception");

        // Act
        _logger.Error("Failed", ex);

        // Assert
        _hub.Received(1).Write(Arg.Is<AlgoLogEvent>(e =>
            e.AlgorithmId == _algoId &&
            e.Level == AlgoLogLevel.Error &&
            e.Message.Contains("Failed") &&
            e.Message.Contains("Test Exception")
        ));
    }

    [Fact]
    public void Debug_ShouldWriteToHub()
    {
        // Act
        _logger.Debug("Hello Debug");

        // Assert
        _hub.Received(1).Write(Arg.Is<AlgoLogEvent>(e =>
            e.AlgorithmId == _algoId &&
            e.Level == AlgoLogLevel.Debug &&
            e.Message == "Hello Debug"
        ));
    }

    [Fact]
    public void Trace_ShouldWriteToHub()
    {
        // Act
        _logger.Trace("Hello Trace");

        // Assert
        _hub.Received(1).Write(Arg.Is<AlgoLogEvent>(e =>
            e.AlgorithmId == _algoId &&
            e.Level == AlgoLogLevel.Trace &&
            e.Message == "Hello Trace"
        ));
    }
}
