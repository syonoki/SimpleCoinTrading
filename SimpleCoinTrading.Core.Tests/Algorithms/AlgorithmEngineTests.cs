using NSubstitute;
using SimpleCoinTrading.Core.Algorithms;
using SimpleCoinTrading.Core.Data;
using SimpleCoinTrading.Core.Logs;
using SimpleCoinTrading.Core.Orders;
using SimpleCoinTrading.Core.Time.Clocks;
using Xunit;

namespace SimpleCoinTrading.Core.Tests.Algorithms;

public class AlgorithmEngineTests
{
    private readonly IMarketDataView _market = Substitute.For<IMarketDataView>();
    private readonly IClock _clock = Substitute.For<IClock>();
    private readonly MarketDataEventBus _bus = new();
    private readonly IOrderOrchestrator _orderOrchestrator = Substitute.For<IOrderOrchestrator>();
    private readonly IAlgorithmLoggerFactory _logFactory = Substitute.For<IAlgorithmLoggerFactory>();
    private readonly AlgorithmEngine _engine;

    public AlgorithmEngineTests()
    {
        _logFactory.Create(Arg.Any<string>()).Returns(Substitute.For<IAlgorithmLogger>());
        _engine = new AlgorithmEngine(
            _market,
            _clock,
            _bus,
            _orderOrchestrator,
            _logFactory);
    }

    [Fact]
    public void StartAlgorithm_ShouldCreateAndInitializeWithUniqueContext()
    {
        // Arrange
        var algo1 = Substitute.For<IAlgorithm>();
        algo1.AlgorithmId.Returns("Algo1");
        
        var algo2 = Substitute.For<IAlgorithm>();
        algo2.AlgorithmId.Returns("Algo2");

        IAlgorithmContext? ctx1 = null;
        algo1.When(x => x.Initialize(Arg.Any<IAlgorithmContext>()))
             .Do(c => ctx1 = c.Arg<IAlgorithmContext>());

        IAlgorithmContext? ctx2 = null;
        algo2.When(x => x.Initialize(Arg.Any<IAlgorithmContext>()))
             .Do(c => ctx2 = c.Arg<IAlgorithmContext>());

        // Act
        _engine.SetupAlgorithm(algo1);
        _engine.StartAlgorithm("Algo1");
        _engine.SetupAlgorithm(algo2);
        _engine.StartAlgorithm("Algo2");
        
        // Assert
        Assert.NotNull(ctx1);
        Assert.NotNull(ctx2);
        Assert.NotSame(ctx1, ctx2);
        Assert.Equal("Algo1", ctx1.AlgorithmId);
        Assert.Equal("Algo2", ctx2.AlgorithmId);
    }

    [Fact]
    public void StopAlgorithm_ShouldDisposeContext()
    {
        // Arrange
        var algo = Substitute.For<IAlgorithm>();
        algo.AlgorithmId.Returns("Algo");
        
        IAlgorithmContext? capturedCtx = null;
        algo.When(x => x.Initialize(Arg.Any<IAlgorithmContext>()))
             .Do(c => capturedCtx = c.Arg<IAlgorithmContext>());

        _engine.SetupAlgorithm(algo);
        _engine.StartAlgorithm("Algo");
        Assert.NotNull(capturedCtx);

        // Act
        _engine.StopAlgorithm("Algo");

        // Assert
        algo.Received(1).Stop();
        Assert.Empty(_engine.Algorithms);
    }
}
